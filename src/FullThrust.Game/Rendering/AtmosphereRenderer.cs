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

}

// Lookup tables for the atmosphere of the body nearest the camera: static tables once per body, the sky view each frame.
public sealed class AtmosphereRenderer : IRenderPass, IFrameUniformsWriter {

    private const int GroupSize = 8;

    private readonly DeviceEx _device;
    private readonly CelestialSystem _system;
    private readonly FloatingOrigin _origin;

    private readonly VkSampler _sampler;
    private readonly VkDescriptorSetLayout _setLayout;
    private readonly VkDescriptorPool _pool;

    private readonly Table _transmittance;
    private readonly Table _multiScatter;
    private readonly Table _irradiance;
    private readonly Table _skyView;

    private CelestialBody? _body;
    private CelestialBody? _built;
    private float3 _center;

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

        _setLayout = Descriptors.CreateLayout(device, [Descriptors.Binding(0, VkDescriptorType.StorageImage, VkShaderStageFlags.ComputeBit)]);
        _pool = Descriptors.CreatePool(device, 4, [new VkDescriptorPoolSize { Type = VkDescriptorType.StorageImage, DescriptorCount = 4 }]);

        _transmittance = new Table(this, shaders, frames, "transmittance_lut.comp", "TransmittanceLut", 256, 64, GlobalBindings.TransmittanceLut);
        _multiScatter = new Table(this, shaders, frames, "multiscatter_lut.comp", "MultiScatterLut", 32, 32, GlobalBindings.MultiScatterLut);
        _irradiance = new Table(this, shaders, frames, "irradiance_lut.comp", "IrradianceLut", 64, 16, GlobalBindings.IrradianceLut);
        _skyView = new Table(this, shaders, frames, "skyview_lut.comp", "SkyViewLut", 192, 108, GlobalBindings.SkyViewLut);

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
            MieExtinction = new float4(mieExtinction, mieExtinction, mieExtinction, 0),
            OzoneAbsorption = new float4(Vector(s.Ozone), (float)s.OzoneCenter),
            GroundAlbedo = new float4(Vector(s.GroundAlbedo), 0),

        };

    }

    private static float3 Vector(double[] v) => v.Length >= 3 ? new float3((float)v[0], (float)v[1], (float)v[2]) : float3.Zero;

    public void Record(in FrameContext frame) {

        var cmd = frame.CommandBuffer;

        _transmittance.Prepare(cmd);
        _multiScatter.Prepare(cmd);
        _irradiance.Prepare(cmd);
        _skyView.Prepare(cmd);

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
        Barriers.ComputeToShaders(cmd);

    }

    public void Dispose() {

        _transmittance.Dispose();
        _multiScatter.Dispose();
        _irradiance.Dispose();
        _skyView.Dispose();

        _device.DestroyDescriptorPool(_pool, null);
        _device.DestroyDescriptorSetLayout(_setLayout, null);
        _device.DestroySampler(_sampler, null);

    }

    // One lookup table: its image, the kernel that fills it and the set binding the image for writing.
    private sealed class Table : IDisposable {

        private readonly RenderTexture _texture;
        private readonly ComputeKernel _kernel;
        private readonly VkDescriptorSet _set;

        public Table(AtmosphereRenderer owner, ShaderLibrary shaders, FrameResources frames, string shader, string name, int width, int height, int binding) {

            _texture = new RenderTexture(owner._device, name, VkFormat.R16G16B16A16SFloat, width, height);
            _kernel = new ComputeKernel(owner._device, shaders, shader, frames.SetLayout, owner._setLayout, ByteSize.Zero);
            _set = Descriptors.Allocate(owner._device, owner._pool, owner._setLayout);

            Descriptors.WriteStorageImage(owner._device, _set, 0, _texture.View);
            frames.WriteImage(binding, _texture.View, owner._sampler, VkImageLayout.General);

        }

        public void Prepare(CommandBuffer cmd) => _texture.Prepare(cmd);

        // A group size of 1 dispatches one workgroup per texel.
        public void Dispatch(CommandBuffer cmd, VkDescriptorSet frameSet, int groupSize, int depth) {

            _kernel.Dispatch(cmd, frameSet, _set, ComputeKernel.Groups(_texture.Width, groupSize), ComputeKernel.Groups(_texture.Height, groupSize), depth);

        }

        public void Dispose() {

            _kernel.Dispose();
            _texture.Dispose();

        }

    }

}
