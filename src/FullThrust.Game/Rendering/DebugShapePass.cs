using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using FullThrust.Core.Ecs;
using FullThrust.Core.Rendering;
using FullThrust.Core.Space;

namespace FullThrust.Game.Rendering;

public sealed class DebugShapePass : IRenderPass {

    [StructLayout(LayoutKind.Sequential)]
    private struct PushData {

        public float4x4 Model;

        public float4 Normal0;
        public float4 Normal1;
        public float4 Normal2;

        public float4 Color;

    }

    private const VkShaderStageFlags PushStages = VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit;

    private readonly DeviceEx _device;

    private readonly World _world;
    private readonly FloatingOrigin _origin;

    private readonly VkPipelineLayout _layout;
    private readonly VkPipeline _pipeline;

    private readonly GpuMesh[] _meshes;

    public unsafe DebugShapePass(DeviceEx device, VkRenderPass renderPass, VkDescriptorSetLayout frameLayout, ShaderLibrary shaders, World world, FloatingOrigin origin) {

        _device = device;
        _world = world;
        _origin = origin;

        _meshes = new GpuMesh[Enum.GetValues<ShapeKind>().Length];
        _meshes[(int)ShapeKind.Box] = new GpuMesh(device, "DebugBox", MeshData.UnitBox());
        _meshes[(int)ShapeKind.Sphere] = new GpuMesh(device, "DebugSphere", MeshData.UnitSphere(96, 192));
        _meshes[(int)ShapeKind.Cylinder] = new GpuMesh(device, "DebugCylinder", MeshData.UnitFrustum(0.5f, 32));
        _meshes[(int)ShapeKind.Cone] = new GpuMesh(device, "DebugCone", MeshData.UnitFrustum(0, 32));
        _meshes[(int)ShapeKind.Frustum] = new GpuMesh(device, "DebugFrustum", MeshData.UnitFrustum(0.22f, 32));

        _layout = device.CreatePipelineLayout([frameLayout], [

            new VkPushConstantRange { StageFlags = PushStages, Offset = ByteSize.Zero, Size = ByteSize.Of<PushData>() },

        ], null);

        _pipeline = Pipelines.Create(device, shaders, new PipelineDesc {

            RenderPass = renderPass,
            Layout = _layout,
            VertexShader = "debug_shape.vert",
            FragmentShader = "debug_shape.frag",
            VertexInput = VertexInput.For<Vertex>(),
            Cull = VkCullModeFlags.None,
            Depth = DepthMode.TestWrite,

        });

    }

    public string Name => "Debug Shapes";

    public RenderStage Stage => RenderStage.Opaque;

    public bool Enabled { get; set; } = true;

    public int DrawCount { get; private set; }

    public void Record(in FrameContext frame) {

        var cmd = frame.CommandBuffer;

        cmd.BindPipeline(VkPipelineBindPoint.Graphics, _pipeline);
        cmd.BindDescriptorSet(VkPipelineBindPoint.Graphics, _layout, 0, frame.FrameSet, []);

        var bound = -1;
        var draws = 0;

        foreach (var row in _world.Query<Transform, DebugShape>()) {

            ref readonly var transform = ref row.C1;
            ref readonly var shape = ref row.C2;

            var kind = (int)shape.Kind;

            if (kind != bound) {

                _meshes[kind].Bind(cmd);
                bound = kind;

            }

            var rotation = float4x4.CreateFromQuaternion(SpaceMath.ToFloat(transform.Rotation));

            var push = new PushData {

                Model = float4x4.CreateScale(shape.Scale) * rotation * float4x4.CreateTranslation(_origin.ToLocal(transform.Position)),

                Normal0 = new float4(rotation.X.X, rotation.X.Y, rotation.X.Z, 0),
                Normal1 = new float4(rotation.Y.X, rotation.Y.Y, rotation.Y.Z, 0),
                Normal2 = new float4(rotation.Z.X, rotation.Z.Y, rotation.Z.Z, 0),

                Color = shape.Color,

            };

            cmd.PushConstants(_layout, PushStages, ByteSize.Zero, push);
            cmd.DrawIndexed(_meshes[kind].IndexCount, 1, 0, 0, 0);

            draws++;

        }

        DrawCount = draws;

    }

    public void Dispose() {

        _device.DestroyPipeline(_pipeline, null);
        _device.DestroyPipelineLayout(_layout, null);

        foreach (var mesh in _meshes) {

            mesh.Dispose();

        }

    }

}
