using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using FullThrust.Core.Bodies;
using FullThrust.Core.Space;
using FullThrust.Core.Terrain;
using FullThrust.Game.Rendering;

namespace FullThrust.Game.Terrain;

[StructLayout(LayoutKind.Sequential)]
public struct TerrainPush {

    // Rotation and translation only, so its upper 3x3 also transforms normals.
    public float4x4 Model;

    // xyz: chunk centre modulo the detail period, so procedural detail is continuous across chunks.
    public float4 Detail;

    // rgb water colours; DeepWater.w is 1 on bodies with an ocean.
    public float4 DeepWater;
    public float4 ShallowWater;

    // Chunk centre in the body-fixed frame.
    public float4 Center;

}

// Quadtree LOD terrain: chunks built on worker threads live in fixed slots of one vertex buffer, recycled LRU first.
public sealed class TerrainRenderer : IRenderPass, IFrameUploader {

    public const double DetailPeriod = 65536;

    private const int SlotCount = 2048;
    private const int JobCount = 96;
    private const int UploadsPerFrame = 24;

    private static readonly int VertexBytes = Marshal.SizeOf<TerrainVertex>();

    private readonly DeviceEx _device;
    private readonly CelestialSystem _system;
    private readonly FloatingOrigin _origin;

    private readonly BodyTerrain[] _bodies;
    private readonly TerrainWorkers _workers;

    private readonly Dictionary<ulong, int> _slotOf = new(SlotCount + JobCount * 2);
    private readonly Slot[] _slots = new Slot[SlotCount];
    private readonly int[] _freeSlots = new int[SlotCount];

    private readonly BufferEx _vertices;
    private readonly BufferEx _indices;

    private readonly BufferEx[] _staging = new BufferEx[Renderer.FramesInFlight];
    private readonly MappedMemory[] _stagingMapped = new MappedMemory[Renderer.FramesInFlight];

    private readonly VkPipelineLayout _layout;
    private readonly VkPipeline _pipeline;

    private readonly float4[] _planes = new float4[5];

    private int _freeCount;
    private int _clock;
    private long _frame;
    private double _time;

    private struct Slot {

        public ulong Key;
        public int Body;
        public long LastUsed;
        public TerrainBounds Bounds;

    }

    public TerrainRenderer(DeviceEx device, GpuUploader uploader, VkRenderPass renderPass, VkDescriptorSetLayout frameLayout, ShaderLibrary shaders, CelestialSystem system, FloatingOrigin origin) {

        _device = device;
        _system = system;
        _origin = origin;

        var bodies = new List<BodyTerrain>();

        foreach (var body in system.Bodies) {

            if (body.Terrain is { } terrain && body.TerrainData is { } data) {

                bodies.Add(new BodyTerrain(this, body, terrain, new TerrainPalette(data)));

            }

        }

        _bodies = bodies.ToArray();
        _workers = new TerrainWorkers(JobCount, Math.Clamp(Environment.ProcessorCount - 3, 1, 4));

        for (var i = 0; i < SlotCount; i++) {

            _freeSlots[i] = SlotCount - 1 - i;

        }

        _freeCount = SlotCount;

        _vertices = device.CreateBuffer(new BufferEx.CreateInfo {

            Name = "TerrainVertices",
            BufferSize = new ByteSize((nuint)(SlotCount * TerrainChunkBuilder.VertexCount * VertexBytes)),
            BufferUsage = VkBufferUsageFlags.VertexBufferBit | VkBufferUsageFlags.TransferDstBit,
            AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,

        });

        _indices = uploader.CreateDeviceBuffer<ushort>("TerrainIndices", TerrainChunkBuilder.Indices, VkBufferUsageFlags.IndexBufferBit);
        uploader.Flush();

        for (var i = 0; i < _staging.Length; i++) {

            _staging[i] = device.CreateBuffer(new BufferEx.CreateInfo {

                Name = "TerrainStaging",
                BufferSize = new ByteSize((nuint)(UploadsPerFrame * TerrainChunkBuilder.VertexCount * VertexBytes)),
                BufferUsage = VkBufferUsageFlags.TransferSrcBit,
                AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,

            });

            _stagingMapped[i] = _staging[i].Map();

        }

        _layout = device.CreatePipelineLayout([frameLayout], [

            new VkPushConstantRange { StageFlags = VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit, Offset = ByteSize.Zero, Size = ByteSize.Of<TerrainPush>() },

        ], null);

        unsafe {

            _pipeline = Pipelines.Create(device, shaders, new PipelineDesc {

                RenderPass = renderPass,
                Layout = _layout,
                VertexShader = "terrain.vert",
                FragmentShader = "terrain.frag",
                VertexInput = VertexInput.For<TerrainVertex>(),

                // Skirts face either way; the terrain itself never shows its back.
                Cull = VkCullModeFlags.None,
                Depth = DepthMode.TestWrite,

            });

        }

    }

    public string Name => "Terrain";

    public RenderStage Stage => RenderStage.Scene;

    public bool Enabled { get; set; } = true;

    // Tints each chunk by its quadtree level.
    public bool ShowLevels { get; set; }

    public int DrawCount { get; private set; }

    public int ResidentChunks => SlotCount - _freeCount;

    public int Uploads { get; private set; }

    public long ChunksBuilt => _workers.Built;

    public int JobsInFlight => _workers.InFlight;

    public IReadOnlyList<BodyTerrain> Bodies => _bodies;

    public float SplitDistance {

        get => (float)(_bodies.Length > 0 ? _bodies[0].Quadtree.SplitDistance : 2.2);

        set {

            foreach (var body in _bodies) {

                body.Quadtree.SplitDistance = value;

            }

        }

    }

    // Chooses the chunks to draw this frame and queues builds for missing ones.
    public void Prepare(double3 camera, double universalTime) {

        _frame++;
        _time = universalTime;

        if (!Enabled) {

            return;

        }

        foreach (var body in _bodies) {

            body.Quadtree.Update(_system.WorldToBodyFixed(body.Body.Index, camera, universalTime), body);

        }

    }

    public void Upload(in FrameContext frame) {

        var cmd = frame.CommandBuffer;
        var staging = _stagingMapped[frame.FrameIndex].AsSpan<TerrainVertex>();
        var uploads = 0;

        while (uploads < UploadsPerFrame && _workers.TryTakeCompleted(out var index)) {

            var job = _workers.Job(index);
            var slot = AllocateSlot();

            if (slot < 0) {

                // Everything resident is in use; drop the chunk and let the quadtree ask again later.
                _slotOf.Remove(job.Key);
                _workers.Return(index);

                continue;

            }

            job.Vertices.AsSpan().CopyTo(staging.Slice(uploads * TerrainChunkBuilder.VertexCount, TerrainChunkBuilder.VertexCount));

            var bytes = (ulong)(TerrainChunkBuilder.VertexCount * VertexBytes);

            cmd.CopyBuffer(_staging[frame.FrameIndex].VkBuffer, _vertices.VkBuffer, [

                new VkBufferCopy { SrcOffset = new ByteSize64((ulong)uploads * bytes), DstOffset = new ByteSize64((ulong)slot * bytes), Size = new ByteSize64(bytes) },

            ]);

            _slots[slot] = new Slot { Key = job.Key, Body = job.Body, LastUsed = _frame, Bounds = job.Bounds };
            _slotOf[job.Key] = slot;
            _workers.Return(index);

            uploads++;

        }

        Uploads = uploads;

        if (uploads > 0) {

            cmd.PipelineBarrier(VkPipelineStageFlags.TransferBit, VkPipelineStageFlags.VertexInputBit, VkDependencyFlags.None, [

                new VkMemoryBarrier { SrcAccessMask = VkAccessFlags.TransferWriteBit, DstAccessMask = VkAccessFlags.VertexAttributeReadBit },

            ], [], []);

        }

    }

    // Free slots first, then the oldest chunk not touched for long enough that no frame in flight can still read it.
    private int AllocateSlot() {

        if (_freeCount > 0) {

            return _freeSlots[--_freeCount];

        }

        for (var i = 0; i < SlotCount; i++) {

            var slot = _clock;

            _clock = (_clock + 1) % SlotCount;

            if (_slots[slot].LastUsed < _frame - Renderer.FramesInFlight - 1) {

                _slotOf.Remove(_slots[slot].Key);

                return slot;

            }

        }

        return -1;

    }

    public void Record(in FrameContext frame) {

        var cmd = frame.CommandBuffer;

        cmd.BindPipeline(VkPipelineBindPoint.Graphics, _pipeline);
        cmd.BindDescriptorSet(VkPipelineBindPoint.Graphics, _layout, 0, frame.FrameSet, []);
        cmd.BindVertexBuffer(0, _vertices.VkBuffer);
        cmd.BindIndexBuffer(_indices.VkBuffer, ByteSize.Zero, VkIndexType.UInt16);

        ExtractPlanes(frame.View * frame.Projection);

        var draws = 0;

        foreach (var body in _bodies) {

            var rotation = body.Body.RotationAt(_time);
            var matrix = float4x4.CreateFromQuaternion(SpaceMath.ToFloat(rotation));
            var position = _system.StateAt(body.Body.Index, _time).Position - _origin.Origin;

            foreach (var node in body.Quadtree.Selected) {

                if (!_slotOf.TryGetValue(Combine(body.Body.Index, node.Key), out var slot) || slot < 0) {

                    continue;

                }

                ref var chunk = ref _slots[slot];

                chunk.LastUsed = _frame;

                var center = position + rotation * chunk.Bounds.Center;
                var local = SpaceMath.ToFloat(center);

                if (!Visible(local, (float)chunk.Bounds.Radius)) {

                    continue;

                }

                var c = chunk.Bounds.Center;
                var detail = new double3(Wrap(c.X), Wrap(c.Y), Wrap(c.Z));

                var push = new TerrainPush {

                    Model = matrix * float4x4.CreateTranslation(local),
                    Detail = new float4(SpaceMath.ToFloat(detail), ShowLevels ? node.Level + 1 : 0),
                    DeepWater = new float4(body.Palette.DeepWater, body.Terrain.HasOcean ? 1 : 0),
                    ShallowWater = new float4(body.Palette.ShallowWater, 0),
                    Center = new float4(SpaceMath.ToFloat(c), 0),

                };

                cmd.PushConstants(_layout, VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit, ByteSize.Zero, push);
                cmd.DrawIndexed(TerrainChunkBuilder.Indices.Length, 1, 0, slot * TerrainChunkBuilder.VertexCount, 0);

                draws++;

            }

        }

        DrawCount = draws;

    }

    private static double Wrap(double value) => value - DetailPeriod * Math.Floor(value / DetailPeriod);

    // Clip-space planes for v * M with reverse-Z: left, right, bottom, top, near. The far plane is at infinity.
    private void ExtractPlanes(in float4x4 m) {

        var c0 = new float4(m.X.X, m.Y.X, m.Z.X, m.W.X);
        var c1 = new float4(m.X.Y, m.Y.Y, m.Z.Y, m.W.Y);
        var c2 = new float4(m.X.Z, m.Y.Z, m.Z.Z, m.W.Z);
        var c3 = new float4(m.X.W, m.Y.W, m.Z.W, m.W.W);

        _planes[0] = c3 + c0;
        _planes[1] = c3 - c0;
        _planes[2] = c3 + c1;
        _planes[3] = c3 - c1;
        _planes[4] = c3 - c2;

        for (var i = 0; i < _planes.Length; i++) {

            var p = _planes[i];
            var length = new float3(p.X, p.Y, p.Z).Length();

            _planes[i] = length > 0 ? p / length : p;

        }

    }

    private bool Visible(float3 center, float radius) {

        foreach (var p in _planes) {

            if (p.X * center.X + p.Y * center.Y + p.Z * center.Z + p.W < -radius) {

                return false;

            }

        }

        return true;

    }

    private static ulong Combine(int body, ulong key) => key | ((ulong)body << 61);

    public void Dispose() {

        _workers.Dispose();

        _device.DestroyPipeline(_pipeline, null);
        _device.DestroyPipelineLayout(_layout, null);

        for (var i = 0; i < _staging.Length; i++) {

            _stagingMapped[i].Dispose();
            _staging[i].Dispose();

        }

        _vertices.Dispose();
        _indices.Dispose();

    }

    // Per-body quadtree; also the chunk source the quadtree asks for bounds and builds.
    public sealed class BodyTerrain : ITerrainChunkSource {

        private readonly TerrainRenderer _owner;

        public BodyTerrain(TerrainRenderer owner, CelestialBody body, ITerrain terrain, TerrainPalette palette) {

            _owner = owner;

            Body = body;
            Terrain = terrain;
            Palette = palette;
            Quadtree = new TerrainQuadtree(terrain, body.Radius, TerrainChunkBuilder.Quads);

        }

        public CelestialBody Body { get; }

        public ITerrain Terrain { get; }

        public TerrainPalette Palette { get; }

        public TerrainQuadtree Quadtree { get; }

        public bool TryGetBounds(in TerrainNode node, out TerrainBounds bounds) {

            if (_owner._slotOf.TryGetValue(Combine(Body.Index, node.Key), out var slot) && slot >= 0) {

                ref var chunk = ref _owner._slots[slot];

                chunk.LastUsed = _owner._frame;
                bounds = chunk.Bounds;

                return true;

            }

            bounds = default;

            return false;

        }

        public bool Request(in TerrainNode node) {

            var key = Combine(Body.Index, node.Key);

            if (_owner._slotOf.ContainsKey(key) || !_owner._workers.TryRent(out var index)) {

                return false;

            }

            var job = _owner._workers.Job(index);

            job.Key = key;
            job.Body = Body.Index;
            job.Node = node;
            job.Terrain = Terrain;
            job.Palette = Palette;
            job.Radius = Body.Radius;

            _owner._slotOf[key] = -1;
            _owner._workers.Submit(index);

            return true;

        }

    }

}
