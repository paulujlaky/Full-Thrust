using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using FullThrust.Core.Bodies;
using FullThrust.Core.Space;

namespace FullThrust.Game.Rendering;

// Mirrors the atmosphere fields of the Frame block in Shaders/common.glsl.
[StructLayout(LayoutKind.Sequential)]
public struct AtmosphereUniforms {

    public float4 Center;
    public float4 Shape;
    public float4 RayleighScattering;
    public float4 MieScattering;
    public float4 MieExtinction;
    public float4 OzoneAbsorption;
    public float4 GroundAlbedo;

    public float4 AerialPerspective;

}

// Lookup tables for the atmosphere of the body nearest the camera: static tables once per body, the sky view each frame.
public sealed class AtmosphereRenderer : IRenderPass, IFrameUniformsWriter {

    private const int GroupSize = 8;

    private const int AerialWidth = 192;
    private const int AerialHeight = 108;
    private const int AerialSlices = 64;

    private const double MarchFromAltitude = 30_000;
    private const double MarchBlend = 10_000;

    private readonly DeviceEx _device;
    private readonly CelestialSystem _system;
    private readonly FloatingOrigin _origin;

    private const int MaxTables = 5;
    private const int MaxOutputs = 6;

    private readonly VkSampler _sampler;
    private readonly VkDescriptorPool _pool;

    private readonly Table _transmittance;
    private readonly Table _multiScatter;
    private readonly Table _irradiance;
    private readonly Table _skyView;
    private readonly Table _aerialPerspective;

    private CelestialBody? _body;
    private CelestialBody? _built;
    private float3 _center;
    private float _aerialDistance;
    private float _marchWeight;

    public AtmosphereRenderer(DeviceEx device, ShaderLibrary shaders, FrameResources frames, CelestialSystem system, FloatingOrigin origin) {

        _device = device;
        _system = system;
        _origin = origin;

        _sampler = device.CreateSampler(new VkSamplerCreateInfo {

            MagFilter = VkFilter.Linear,
            MinFilter = VkFilter.Linear,
            MipmapMode = VkSamplerMipmapMode.Nearest,
            AddressModeU = VkSamplerAddressMode.ClampToEdge,
            AddressModeV = VkSamplerAddressMode.ClampToEdge,
            AddressModeW = VkSamplerAddressMode.ClampToEdge,

        }, null);

        _pool = Descriptors.CreatePool(device, MaxTables, [new VkDescriptorPoolSize { Type = VkDescriptorType.StorageImage, DescriptorCount = MaxOutputs }]);

        _transmittance = new Table(this, shaders, frames, "transmittance_lut.comp", new Output("TransmittanceLut", 256, 64, 1, GlobalBindings.TransmittanceLut));
        _multiScatter = new Table(this, shaders, frames, "multiscatter_lut.comp", new Output("MultiScatterLut", 32, 32, 1, GlobalBindings.MultiScatterLut));
        _irradiance = new Table(this, shaders, frames, "irradiance_lut.comp", new Output("IrradianceLut", 64, 16, 1, GlobalBindings.IrradianceLut));
        _skyView = new Table(this, shaders, frames, "skyview_lut.comp", new Output("SkyViewLut", 192, 108, 1, GlobalBindings.SkyViewLut));

        _aerialPerspective = new Table(this, shaders, frames, "aerial_perspective.comp",
            new Output("AerialPerspective", AerialWidth, AerialHeight, AerialSlices, GlobalBindings.AerialPerspective),
            new Output("SkyShafts", AerialWidth, AerialHeight, 1, GlobalBindings.SkyShafts));

    }

    public string Name => "Atmosphere";

    public RenderStage Stage => RenderStage.Prepare;

    // Stays on so the tables are always valid to sample; Active switches the effect.
    public bool Enabled { get; set; } = true;

    public bool Active { get; set; } = true;

    public CelestialBody? Body => _body;

    // Picks the body whose atmosphere the camera is nearest, relative to its size.
    public void Update(double3 camera, double universalTime) {

        CelestialBody? best = null;
        var bestRatio = double.MaxValue;

        foreach (var body in _system.Bodies) {

            if (body.Atmosphere?.Scattering is null) {

                continue;

            }

            var position = _system.StateAt(body.Index, universalTime).Position;
            var ratio = (camera - position).Length() / body.Radius;

            if (ratio < bestRatio) {

                best = body;
                bestRatio = ratio;

            }

        }

        _body = best;

        if (best is not null) {

            _center = SpaceMath.ToFloat(_system.StateAt(best.Index, universalTime).Position - _origin.Origin);

            // Far enough to reach the far edge of the atmosphere past the horizon.
            var r = (camera - _system.StateAt(best.Index, universalTime).Position).Length();
            var top = best.Radius + best.Atmosphere!.Scattering!.Height;

            _aerialDistance = (float)(Math.Sqrt(Math.Max(r * r - best.Radius * best.Radius, 0)) + Math.Sqrt(top * top - best.Radius * best.Radius));

            // Froxels lose the steep density gradient along long downward paths, so high cameras march per pixel.
            _marchWeight = (float)Math.Clamp((r - best.Radius - MarchFromAltitude) / MarchBlend + 0.5, 0, 1);

        }

    }

    public void Fill(ref FrameUniforms uniforms, bool enabled) {

        if (_body?.Atmosphere?.Scattering is not { } s || !enabled || !Active) {

            uniforms.Atmosphere = default;

            return;

        }

        var radius = (float)_body.Radius;
        var mie = (float)s.MieScattering;
        var mieExtinction = (float)(s.MieScattering + s.MieAbsorption);

        uniforms.Atmosphere = new AtmosphereUniforms {

            Center = new float4(_center, radius),
            Shape = new float4(radius + (float)s.Height, (float)s.MieAnisotropy, 1, (float)s.OzoneHalfWidth),
            RayleighScattering = new float4(Vector(s.Rayleigh), (float)s.RayleighScaleHeight),
            MieScattering = new float4(mie, mie, mie, (float)s.MieScaleHeight),
            MieExtinction = new float4(mieExtinction, mieExtinction, mieExtinction, (float)s.BoundaryLayer),
            OzoneAbsorption = new float4(Vector(s.Ozone), (float)s.OzoneCenter),
            GroundAlbedo = new float4(Vector(s.GroundAlbedo), (float)s.BoundaryLayerHeight),
            AerialPerspective = new float4(_aerialDistance, AerialSlices, 1, _marchWeight),

        };

    }

    private static float3 Vector(double[] v) => v.Length >= 3 ? new float3((float)v[0], (float)v[1], (float)v[2]) : float3.Zero;

    public void Record(in FrameContext frame) {

        var cmd = frame.CommandBuffer;

        _transmittance.Prepare(cmd);
        _multiScatter.Prepare(cmd);
        _irradiance.Prepare(cmd);
        _skyView.Prepare(cmd);
        _aerialPerspective.Prepare(cmd);

        if (_body is null || !Active) {

            return;

        }

        Barriers.ShadersToCompute(cmd);

        // The static tables depend only on the body, so they are rebuilt when the nearest atmosphere changes.
        if (_built != _body) {

            _transmittance.Dispatch(cmd, frame.FrameSet, GroupSize, 1);
            Barriers.ComputeToShaders(cmd);
            _multiScatter.Dispatch(cmd, frame.FrameSet, 1, 1);
            Barriers.ComputeToShaders(cmd);
            _irradiance.Dispatch(cmd, frame.FrameSet, GroupSize, 1);
            Barriers.ComputeToShaders(cmd);

            _built = _body;

        }

        _skyView.Dispatch(cmd, frame.FrameSet, GroupSize, 1);
        _aerialPerspective.Dispatch(cmd, frame.FrameSet, GroupSize, 1);
        Barriers.ComputeToShaders(cmd);

    }

    public void Dispose() {

        _transmittance.Dispose();
        _multiScatter.Dispose();
        _irradiance.Dispose();
        _skyView.Dispose();
        _aerialPerspective.Dispose();

        _device.DestroyDescriptorPool(_pool, null);
        _device.DestroySampler(_sampler, null);

    }

    // An image a table's kernel writes at set 1 in order, and the global binding every shader samples it through.
    private readonly record struct Output(string Name, int Width, int Height, int Depth, int Binding);

    // One lookup-table kernel and the images it fills; the first output sets the dispatch size.
    private sealed class Table : IDisposable {

        private readonly DeviceEx _device;
        private readonly RenderTexture[] _textures;
        private readonly VkDescriptorSetLayout _layout;
        private readonly ComputeKernel _kernel;
        private readonly VkDescriptorSet _set;

        public Table(AtmosphereRenderer owner, ShaderLibrary shaders, FrameResources frames, string shader, params Output[] outputs) {

            _device = owner._device;
            _textures = new RenderTexture[outputs.Length];

            var bindings = new VkDescriptorSetLayoutBinding[outputs.Length];

            for (var i = 0; i < outputs.Length; i++) {

                bindings[i] = Descriptors.Binding(i, VkDescriptorType.StorageImage, VkShaderStageFlags.ComputeBit);

            }

            _layout = Descriptors.CreateLayout(_device, bindings);
            _kernel = new ComputeKernel(_device, shaders, shader, frames.SetLayout, _layout, ByteSize.Zero);
            _set = Descriptors.Allocate(_device, owner._pool, _layout);

            for (var i = 0; i < outputs.Length; i++) {

                var output = outputs[i];

                _textures[i] = new RenderTexture(_device, output.Name, VkFormat.R16G16B16A16SFloat, output.Width, output.Height, output.Depth);

                Descriptors.WriteStorageImage(_device, _set, i, _textures[i].View);
                frames.WriteImage(output.Binding, _textures[i].View, owner._sampler, VkImageLayout.General);

            }

        }

        public void Prepare(CommandBuffer cmd) {

            foreach (var texture in _textures) {

                texture.Prepare(cmd);

            }

        }

        // A group size of 1 dispatches one workgroup per texel.
        public void Dispatch(CommandBuffer cmd, VkDescriptorSet frameSet, int groupSize, int depth) {

            _kernel.Dispatch(cmd, frameSet, _set, ComputeKernel.Groups(_textures[0].Width, groupSize), ComputeKernel.Groups(_textures[0].Height, groupSize), depth);

        }

        public void Dispose() {

            _kernel.Dispose();

            foreach (var texture in _textures) {

                texture.Dispose();

            }

            _device.DestroyDescriptorSetLayout(_layout, null);

        }

    }

}
