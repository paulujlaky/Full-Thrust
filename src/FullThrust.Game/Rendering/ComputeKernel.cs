using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

// A compute shader and its pipeline layout: set 0 is the frame set, set 1 the kernel's own resources.
public sealed class ComputeKernel : IDisposable {

    private readonly DeviceEx _device;

    public ComputeKernel(DeviceEx device, ShaderLibrary shaders, string shader, VkDescriptorSetLayout frameLayout, VkDescriptorSetLayout setLayout, ByteSize pushSize) {

        _device = device;

        Layout = pushSize == ByteSize.Zero
            ? device.CreatePipelineLayout([frameLayout, setLayout], [], null)
            : device.CreatePipelineLayout([frameLayout, setLayout], [new VkPushConstantRange { StageFlags = VkShaderStageFlags.ComputeBit, Offset = ByteSize.Zero, Size = pushSize }], null);

        var module = shaders.Load(shader);

        try {

            Pipeline = device.CreateComputePipeline(default, new VkComputePipelineCreateInfo {

                Stage = Presets.ShaderStage.Compute(module),
                Layout = Layout,

            }, null);

        } finally {

            device.DestroyShaderModule(module, null);

        }

    }

    public VkPipelineLayout Layout { get; }

    public VkPipeline Pipeline { get; }

    public static int Groups(int size, int groupSize) => (size + groupSize - 1) / groupSize;

    public void Dispatch<T>(CommandBuffer cmd, VkDescriptorSet frameSet, VkDescriptorSet set, in T push, int x, int y, int z) where T : unmanaged {

        Bind(cmd, frameSet, set);
        cmd.PushConstants(Layout, VkShaderStageFlags.ComputeBit, ByteSize.Zero, push);
        cmd.Dispatch(x, y, z);

    }

    public void Dispatch(CommandBuffer cmd, VkDescriptorSet frameSet, VkDescriptorSet set, int x, int y, int z) {

        Bind(cmd, frameSet, set);
        cmd.Dispatch(x, y, z);

    }

    private void Bind(CommandBuffer cmd, VkDescriptorSet frameSet, VkDescriptorSet set) {

        cmd.BindPipeline(VkPipelineBindPoint.Compute, Pipeline);
        cmd.BindDescriptorSet(VkPipelineBindPoint.Compute, Layout, 0, frameSet, []);
        cmd.BindDescriptorSet(VkPipelineBindPoint.Compute, Layout, 1, set, []);

    }

    public void Dispose() {

        _device.DestroyPipeline(Pipeline, null);
        _device.DestroyPipelineLayout(Layout, null);

    }

}

public static class Barriers {

    // Compute writes visible to later compute and fragment shader reads.
    public static void ComputeToShaders(CommandBuffer cmd) {

        cmd.PipelineBarrier(VkPipelineStageFlags.ComputeShaderBit, VkPipelineStageFlags.ComputeShaderBit | VkPipelineStageFlags.FragmentShaderBit, VkDependencyFlags.None, [

            new VkMemoryBarrier { SrcAccessMask = VkAccessFlags.ShaderWriteBit, DstAccessMask = VkAccessFlags.ShaderReadBit | VkAccessFlags.ShaderWriteBit },

        ], [], []);

    }

    // Fragment and compute reads of the previous frame finish before compute overwrites the image.
    public static void ShadersToCompute(CommandBuffer cmd) {

        cmd.PipelineBarrier(VkPipelineStageFlags.ComputeShaderBit | VkPipelineStageFlags.FragmentShaderBit, VkPipelineStageFlags.ComputeShaderBit, VkDependencyFlags.None, [], [], []);

    }

}
