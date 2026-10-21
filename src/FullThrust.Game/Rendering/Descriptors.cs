using Brutal;
using Brutal.VulkanApi;

namespace FullThrust.Game.Rendering;

public static class Descriptors {

    public static VkDescriptorSetLayoutBinding Binding(int binding, VkDescriptorType type, VkShaderStageFlags stages, int count = 1) {

        return new VkDescriptorSetLayoutBinding { Binding = binding, DescriptorType = type, DescriptorCount = count, StageFlags = stages };

    }

    public static unsafe VkDescriptorSetLayout CreateLayout(Device device, ReadOnlySpan<VkDescriptorSetLayoutBinding> bindings) {

        fixed (VkDescriptorSetLayoutBinding* pBindings = bindings) {

            return device.CreateDescriptorSetLayout(new VkDescriptorSetLayoutCreateInfo {

                BindingCount = bindings.Length,
                Bindings = pBindings,

            }, null);

        }

    }

    public static unsafe VkDescriptorPool CreatePool(Device device, int maxSets, ReadOnlySpan<VkDescriptorPoolSize> sizes) {

        fixed (VkDescriptorPoolSize* pSizes = sizes) {

            return device.CreateDescriptorPool(new VkDescriptorPoolCreateInfo {

                MaxSets = maxSets,
                PoolSizeCount = sizes.Length,
                PoolSizes = pSizes,

            }, null);

        }

    }

    public static unsafe VkDescriptorSet Allocate(Device device, VkDescriptorPool pool, VkDescriptorSetLayout layout) {

        Span<VkDescriptorSet> sets = stackalloc VkDescriptorSet[1];

        device.AllocateDescriptorSets(new VkDescriptorSetAllocateInfo {

            DescriptorPool = pool,
            DescriptorSetCount = 1,
            SetLayouts = &layout,

        }, sets);

        return sets[0];

    }

    public static unsafe void WriteUniformBuffer(Device device, VkDescriptorSet set, int binding, VkBuffer buffer, ByteSize size) {

        var info = new VkDescriptorBufferInfo { Buffer = buffer, Offset = default, Range = size };

        device.UpdateDescriptorSets([

            new VkWriteDescriptorSet {

                DstSet = set,
                DstBinding = binding,
                DescriptorCount = 1,
                DescriptorType = VkDescriptorType.UniformBuffer,
                BufferInfo = &info,

            },

        ], []);

    }

    public static unsafe void WriteStorageBuffer(Device device, VkDescriptorSet set, int binding, VkBuffer buffer, ByteSize size) {

        var info = new VkDescriptorBufferInfo { Buffer = buffer, Offset = default, Range = size };

        device.UpdateDescriptorSets([

            new VkWriteDescriptorSet {

                DstSet = set,
                DstBinding = binding,
                DescriptorCount = 1,
                DescriptorType = VkDescriptorType.StorageBuffer,
                BufferInfo = &info,

            },

        ], []);

    }

    public static unsafe void WriteStorageImage(Device device, VkDescriptorSet set, int binding, VkImageView view) {

        var info = new VkDescriptorImageInfo { ImageView = view, ImageLayout = VkImageLayout.General };

        device.UpdateDescriptorSets([

            new VkWriteDescriptorSet {

                DstSet = set,
                DstBinding = binding,
                DescriptorCount = 1,
                DescriptorType = VkDescriptorType.StorageImage,
                ImageInfo = &info,

            },

        ], []);

    }

    public static unsafe void WriteImage(Device device, VkDescriptorSet set, int binding, VkImageView view, VkSampler sampler, VkImageLayout layout = VkImageLayout.ShaderReadOnlyOptimal) {

        var info = new VkDescriptorImageInfo { ImageView = view, Sampler = sampler, ImageLayout = layout };

        device.UpdateDescriptorSets([

            new VkWriteDescriptorSet {

                DstSet = set,
                DstBinding = binding,
                DescriptorCount = 1,
                DescriptorType = VkDescriptorType.CombinedImageSampler,
                ImageInfo = &info,

            },

        ], []);

    }

}
