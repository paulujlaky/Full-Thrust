using Brutal.Numerics;
using Brutal.VulkanApi;

namespace FullThrust.Game.Rendering;

// Scene passes draw linear HDR radiance; Post and Overlay passes draw into the swapchain after it.
public enum RenderStage {

    Scene,
    Post,
    Overlay,

}

public readonly struct FrameContext {

    public readonly CommandBuffer CommandBuffer;

    public readonly int FrameIndex;

    public readonly VkExtent2D Extent;

    public readonly float4x4 View;
    public readonly float4x4 Projection;

    // Set 0 for any pipeline built against FrameResources.SetLayout.
    public readonly VkDescriptorSet FrameSet;

    public FrameContext(CommandBuffer commandBuffer, int frameIndex, VkExtent2D extent, float4x4 view, float4x4 projection, VkDescriptorSet frameSet) {

        CommandBuffer = commandBuffer;
        FrameIndex = frameIndex;
        Extent = extent;
        View = view;
        Projection = projection;
        FrameSet = frameSet;

    }

}

public interface IRenderPass : IDisposable {

    string Name { get; }

    RenderStage Stage { get; }

    bool Enabled { get; set; }

    void Record(in FrameContext frame);

}

// Passes that keep resolution-dependent resources hear about swapchain and HDR target rebuilds.
public interface IResizeListener {

    void OnResize(VkExtent2D extent);

}

// Passes that copy data to the GPU each frame; called before any render pass begins, where transfers are not allowed.
public interface IFrameUploader {

    void Upload(in FrameContext frame);

}
