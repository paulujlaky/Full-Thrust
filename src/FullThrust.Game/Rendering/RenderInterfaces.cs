using Brutal.Numerics;
using Brutal.VulkanApi;

namespace FullThrust.Game.Rendering;

public readonly struct FrameContext {

    public readonly CommandBuffer CommandBuffer;

    public readonly int FrameIndex;

    public readonly VkExtent2D Extent;

    public readonly float4x4 View;
    public readonly float4x4 Projection;

    public FrameContext(CommandBuffer commandBuffer, int frameIndex, VkExtent2D extent, float4x4 view, float4x4 projection) {

        CommandBuffer = commandBuffer;
        FrameIndex = frameIndex;
        Extent = extent;
        View = view;
        Projection = projection;

    }

}

public interface IRenderPass : IDisposable {

    string Name { get; }

    bool Enabled { get; set; }

    void Record(in FrameContext frame);

}
