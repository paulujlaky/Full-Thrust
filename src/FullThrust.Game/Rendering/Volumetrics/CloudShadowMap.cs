using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering.Volumetrics;

// Sunlight left after crossing the nearest cloud layer, marched through its full density on a sun-aligned grid around the camera.
public sealed class CloudShadowMap : IRenderPass {

    // Covers CloudShadowExtent in common.glsl, where surfaces, haze and the sky look it up.
    private const int Size = 384;
    private const int GroupSize = 8;

    // CloudShadowFadeEnd in common.glsl: from higher up every visible surface takes the weather map instead.
    private const double MaxHeightAbove = 40_000;

    private readonly DeviceEx _device;
    private readonly VolumeNoise _noise;
    private readonly CloudSource _clouds;

    private readonly RenderTexture _texture;
    private readonly VkSampler _sampler;
    private readonly VkDescriptorSetLayout _layout;
    private readonly VkDescriptorPool _pool;
    private readonly VkDescriptorSet _set;
    private readonly ComputeKernel _kernel;

    public CloudShadowMap(DeviceEx device, ShaderLibrary shaders, FrameResources frames, VolumeNoise noise, CloudSource clouds) {

        _device = device;
        _noise = noise;
        _clouds = clouds;

        _texture = new RenderTexture(device, "CloudShadowMap", VkFormat.R16SFloat, Size, Size, 1);

        _sampler = device.CreateSampler(new VkSamplerCreateInfo {

            MagFilter = VkFilter.Linear,
            MinFilter = VkFilter.Linear,
            MipmapMode = VkSamplerMipmapMode.Nearest,
            AddressModeU = VkSamplerAddressMode.ClampToEdge,
            AddressModeV = VkSamplerAddressMode.ClampToEdge,
            AddressModeW = VkSamplerAddressMode.ClampToEdge,

        }, null);

        // Bindings follow volumetric.glsl, whose cloud density the kernel shares.
        _layout = Descriptors.CreateLayout(device, [

            Descriptors.Binding(1, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.ComputeBit),
            Descriptors.Binding(2, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.ComputeBit),
            Descriptors.Binding(7, VkDescriptorType.StorageImage, VkShaderStageFlags.ComputeBit),

        ]);

        _pool = Descriptors.CreatePool(device, 1, [

            new VkDescriptorPoolSize { Type = VkDescriptorType.CombinedImageSampler, DescriptorCount = 2 },
            new VkDescriptorPoolSize { Type = VkDescriptorType.StorageImage, DescriptorCount = 1 },

        ]);

        _set = Descriptors.Allocate(device, _pool, _layout);

        Descriptors.WriteImage(device, _set, 1, noise.Shape, noise.Sampler, VkImageLayout.General);
        Descriptors.WriteImage(device, _set, 2, noise.Detail, noise.Sampler, VkImageLayout.General);
        Descriptors.WriteStorageImage(device, _set, 7, _texture.View);

        frames.WriteImage(GlobalBindings.CloudShadowMap, _texture.View, _sampler, VkImageLayout.General);

        _kernel = new ComputeKernel(device, shaders, "cloud_shadow.comp", frames.SetLayout, _layout, ByteSize.Of<VolumePrimitive>());

    }

    public string Name => "Cloud shadows";

    public RenderStage Stage => RenderStage.Prepare;

    public bool Enabled { get; set; } = true;

    public void Record(in FrameContext frame) {

        var cmd = frame.CommandBuffer;

        _texture.Prepare(cmd);

        if (!_clouds.Visible || !_clouds.HasLayer || _clouds.HeightAbove > MaxHeightAbove) {

            return;

        }

        _noise.Prepare(cmd, frame.FrameSet);
        Barriers.ShadersToCompute(cmd);

        var groups = ComputeKernel.Groups(Size, GroupSize);

        _kernel.Dispatch(cmd, frame.FrameSet, _set, _clouds.Nearest, groups, groups, 1);
        Barriers.ComputeToShaders(cmd);

    }

    public void Dispose() {

        _kernel.Dispose();
        _texture.Dispose();

        _device.DestroyDescriptorPool(_pool, null);
        _device.DestroyDescriptorSetLayout(_layout, null);
        _device.DestroySampler(_sampler, null);

    }

}
