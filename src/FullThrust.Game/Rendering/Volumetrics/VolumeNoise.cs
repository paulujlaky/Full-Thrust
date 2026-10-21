using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering.Volumetrics;

// Tileable 3D noise every volume samples: large Perlin-Worley shapes and fine Worley detail, generated once on the GPU.
public sealed class VolumeNoise : IDisposable {

    private const int ShapeSize = 128;
    private const int DetailSize = 32;
    private const int GroupSize = 4;

    private readonly DeviceEx _device;

    private readonly RenderTexture _shape;
    private readonly RenderTexture _detail;

    private readonly ComputeKernel _shapeKernel;
    private readonly ComputeKernel _detailKernel;

    private readonly VkDescriptorSetLayout _setLayout;
    private readonly VkDescriptorPool _pool;
    private readonly VkDescriptorSet _shapeSet;
    private readonly VkDescriptorSet _detailSet;

    private bool _generated;

    public VolumeNoise(DeviceEx device, ShaderLibrary shaders, VkDescriptorSetLayout frameLayout) {

        _device = device;

        _shape = new RenderTexture(device, "VolumeShapeNoise", VkFormat.R8G8B8A8UNorm, ShapeSize, ShapeSize, ShapeSize);
        _detail = new RenderTexture(device, "VolumeDetailNoise", VkFormat.R8G8B8A8UNorm, DetailSize, DetailSize, DetailSize);

        _setLayout = Descriptors.CreateLayout(device, [Descriptors.Binding(0, VkDescriptorType.StorageImage, VkShaderStageFlags.ComputeBit)]);
        _pool = Descriptors.CreatePool(device, 2, [new VkDescriptorPoolSize { Type = VkDescriptorType.StorageImage, DescriptorCount = 2 }]);

        _shapeSet = Descriptors.Allocate(device, _pool, _setLayout);
        _detailSet = Descriptors.Allocate(device, _pool, _setLayout);

        Descriptors.WriteStorageImage(device, _shapeSet, 0, _shape.View);
        Descriptors.WriteStorageImage(device, _detailSet, 0, _detail.View);

        _shapeKernel = new ComputeKernel(device, shaders, "volume_shape_noise.comp", frameLayout, _setLayout, ByteSize.Zero);
        _detailKernel = new ComputeKernel(device, shaders, "volume_detail_noise.comp", frameLayout, _setLayout, ByteSize.Zero);

        Sampler = device.CreateSampler(new VkSamplerCreateInfo {

            MagFilter = VkFilter.Linear,
            MinFilter = VkFilter.Linear,
            MipmapMode = VkSamplerMipmapMode.Nearest,
            AddressModeU = VkSamplerAddressMode.Repeat,
            AddressModeV = VkSamplerAddressMode.Repeat,
            AddressModeW = VkSamplerAddressMode.Repeat,

        }, null);

    }

    public VkImageView Shape => _shape.View;

    public VkImageView Detail => _detail.View;

    public VkSampler Sampler { get; }

    // Records the one-off generation the first time it is called.
    public void Prepare(CommandBuffer cmd, VkDescriptorSet frameSet) {

        _shape.Prepare(cmd);
        _detail.Prepare(cmd);

        if (_generated) {

            return;

        }

        var shapeGroups = ComputeKernel.Groups(ShapeSize, GroupSize);
        var detailGroups = ComputeKernel.Groups(DetailSize, GroupSize);

        _shapeKernel.Dispatch(cmd, frameSet, _shapeSet, shapeGroups, shapeGroups, shapeGroups);
        _detailKernel.Dispatch(cmd, frameSet, _detailSet, detailGroups, detailGroups, detailGroups);
        Barriers.ComputeToShaders(cmd);

        _generated = true;

    }

    public void Dispose() {

        _shapeKernel.Dispose();
        _detailKernel.Dispose();
        _shape.Dispose();
        _detail.Dispose();

        _device.DestroySampler(Sampler, null);
        _device.DestroyDescriptorPool(_pool, null);
        _device.DestroyDescriptorSetLayout(_setLayout, null);

    }

}
