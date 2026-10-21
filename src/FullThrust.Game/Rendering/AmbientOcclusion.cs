using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct AoPush {

    public float4 Projection;
    public float4 Params;
    public int4 Size;

}

// Screen-space ambient occlusion from the prepass depth (GTAO), filtered and published to every scene shader.
public sealed class AmbientOcclusion : IRenderPass, IResizeListener, IFrameUniformsWriter {

    private const int GroupSize = 8;

    private readonly DeviceEx _device;
    private readonly HdrTarget _target;
    private readonly FrameResources _frames;

    private readonly VkSampler _pointSampler;
    private readonly VkDescriptorSetLayout _setLayout;
    private readonly VkDescriptorPool _pool;
    private readonly VkDescriptorSet _set;

    private readonly ComputeKernel _occlusion;
    private readonly ComputeKernel _filter;

    private readonly RenderTexture _raw;
    private readonly RenderTexture _filtered;

    public AmbientOcclusion(DeviceEx device, ShaderLibrary shaders, HdrTarget target, FrameResources frames) {

        _device = device;
        _target = target;
        _frames = frames;

        _pointSampler = device.CreateSampler(new VkSamplerCreateInfo {

            MagFilter = VkFilter.Nearest,
            MinFilter = VkFilter.Nearest,
            MipmapMode = VkSamplerMipmapMode.Nearest,
            AddressModeU = VkSamplerAddressMode.ClampToEdge,
            AddressModeV = VkSamplerAddressMode.ClampToEdge,
            AddressModeW = VkSamplerAddressMode.ClampToEdge,

        }, null);

        _setLayout = Descriptors.CreateLayout(device, [

            Descriptors.Binding(0, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.ComputeBit),
            Descriptors.Binding(1, VkDescriptorType.StorageImage, VkShaderStageFlags.ComputeBit),
            Descriptors.Binding(2, VkDescriptorType.StorageImage, VkShaderStageFlags.ComputeBit),

        ]);

        _pool = Descriptors.CreatePool(device, 1, [

            new VkDescriptorPoolSize { Type = VkDescriptorType.CombinedImageSampler, DescriptorCount = 1 },
            new VkDescriptorPoolSize { Type = VkDescriptorType.StorageImage, DescriptorCount = 2 },

        ]);

        _set = Descriptors.Allocate(device, _pool, _setLayout);

        _occlusion = new ComputeKernel(device, shaders, "gtao.comp", frames.SetLayout, _setLayout, ByteSize.Of<AoPush>());
        _filter = new ComputeKernel(device, shaders, "ao_filter.comp", frames.SetLayout, _setLayout, ByteSize.Of<AoPush>());

        var extent = target.Extent;

        _raw = new RenderTexture(device, "AoRaw", VkFormat.R8UNorm, extent.Width, extent.Height);
        _filtered = new RenderTexture(device, "AoFiltered", VkFormat.R8UNorm, extent.Width, extent.Height);

        WriteDescriptors();

    }

    public string Name => "Ambient occlusion";

    public RenderStage Stage => RenderStage.AfterDepth;

    // Stays on so the occlusion image is always valid to sample; Active switches the effect.
    public bool Enabled { get; set; } = true;

    public bool Active { get; set; } = true;

    public float Radius { get; set; } = 1.5f;

    public float Falloff { get; set; } = 0.6f;

    public float Power { get; set; } = 1.4f;

    public float MaxRadiusPixels { get; set; } = 96.0f;

    public bool ShowOcclusion { get; set; }

    public void OnResize(VkExtent2D extent) {

        _raw.Resize(extent.Width, extent.Height);
        _filtered.Resize(extent.Width, extent.Height);

        WriteDescriptors();

    }

    private void WriteDescriptors() {

        Descriptors.WriteImage(_device, _set, 0, _target.DepthView, _pointSampler, VkImageLayout.DepthStencilReadOnlyOptimal);
        Descriptors.WriteStorageImage(_device, _set, 1, _raw.View);
        Descriptors.WriteStorageImage(_device, _set, 2, _filtered.View);

        _frames.WriteImage(GlobalBindings.AmbientOcclusion, _filtered.View, _pointSampler, VkImageLayout.General);

    }

    public void Fill(ref FrameUniforms uniforms, bool enabled) {

        uniforms.AmbientOcclusion = new float4(enabled && Active ? 1 : 0, ShowOcclusion ? 1 : 0, 0, 0);

    }

    public void Record(in FrameContext frame) {

        var cmd = frame.CommandBuffer;

        _raw.Prepare(cmd);
        _filtered.Prepare(cmd);

        if (!Active) {

            return;

        }

        var projection = frame.Projection;

        var push = new AoPush {

            Projection = new float4(projection.X.X, MathF.Abs(projection.Y.Y), projection.W.Z, 0),
            Params = new float4(Radius, Falloff, Power, MaxRadiusPixels),
            Size = new int4(frame.Extent.Width, frame.Extent.Height, 0, 0),

        };

        var x = ComputeKernel.Groups(frame.Extent.Width, GroupSize);
        var y = ComputeKernel.Groups(frame.Extent.Height, GroupSize);

        Barriers.ShadersToCompute(cmd);
        _occlusion.Dispatch(cmd, frame.FrameSet, _set, push, x, y, 1);
        Barriers.ComputeToShaders(cmd);
        _filter.Dispatch(cmd, frame.FrameSet, _set, push, x, y, 1);
        Barriers.ComputeToShaders(cmd);

    }

    public void Dispose() {

        _occlusion.Dispose();
        _filter.Dispose();
        _raw.Dispose();
        _filtered.Dispose();

        _device.DestroyDescriptorPool(_pool, null);
        _device.DestroyDescriptorSetLayout(_setLayout, null);
        _device.DestroySampler(_pointSampler, null);

    }

}
