using Brutal.Numerics;
using Brutal.VulkanApi;

namespace FullThrust.Game.Rendering;

// Frame order. Stages named After* or Prepare/PostProcess run outside any render pass, so passes there
// record their own render passes or compute; the others run inside the render pass the renderer opens.
public enum RenderStage {

    // Shadow maps, lookup tables and other work the scene samples.
    Prepare,

    // Depth-only prepass into the HDR depth buffer.
    Depth,

    // Reads scene depth, e.g. ambient occlusion.
    AfterDepth,

    // HDR colour with the prepass depth.
    Opaque,

    // Reads opaque colour and depth, e.g. volumetrics.
    AfterOpaque,

    // HDR colour over read-only depth.
    Transparent,

    // HDR to HDR effects before tonemapping.
    PostProcess,

    // Swapchain.
    Post,
    Overlay,

}

public static class RenderStages {

    public static bool IsOutsideRenderPass(RenderStage stage) {

        return stage is RenderStage.Prepare or RenderStage.AfterDepth or RenderStage.AfterOpaque or RenderStage.PostProcess;

    }

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

public enum DepthView {

    Camera,
    Shadow,

}

public readonly struct DepthContext {

    public readonly CommandBuffer CommandBuffer;

    public readonly VkDescriptorSet FrameSet;

    public readonly DepthView View;

    // Shadow cascade index, or -1 for the camera.
    public readonly int Cascade;

    public readonly Frustum Frustum;

    public DepthContext(CommandBuffer commandBuffer, VkDescriptorSet frameSet, DepthView view, int cascade, in Frustum frustum) {

        CommandBuffer = commandBuffer;
        FrameSet = frameSet;
        View = view;
        Cascade = cascade;
        Frustum = frustum;

    }

}

// Geometry that writes depth for the camera prepass and the sun's shadow cascades.
public interface IDepthCaster {

    bool CastsDepth { get; }

    void RecordDepth(in DepthContext context);

}
