using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

// Linear HDR colour and depth the scene passes draw into; the tonemap pass resolves it to the swapchain.
public sealed class HdrTarget : IDisposable {

    public const VkFormat ColorFormat = VkFormat.R16G16B16A16SFloat;
    public const VkFormat DepthFormat = VkFormat.D32SFloat;

    private readonly DeviceEx _device;

    private ImageEx? _color;
    private ImageViewEx _colorView;

    private ImageEx? _depth;
    private ImageViewEx _depthView;

    public HdrTarget(DeviceEx device, VkExtent2D extent) {

        _device = device;

        RenderPass = CreateRenderPass();
        Create(extent);

    }

    public VkRenderPass RenderPass { get; }

    public VkFramebuffer Framebuffer { get; private set; }

    public VkExtent2D Extent { get; private set; }

    public VkImageView ColorView => _colorView.VkImageView;

    public void Resize(VkExtent2D extent) {

        DestroyTargets();
        Create(extent);

    }

    private unsafe void Create(VkExtent2D extent) {

        Extent = extent;

        var color = CreateImage("HdrColor", ColorFormat, VkImageUsageFlags.ColorAttachmentBit | VkImageUsageFlags.SampledBit);
        var depth = CreateImage("HdrDepth", DepthFormat, VkImageUsageFlags.DepthStencilAttachmentBit);

        _color = color;
        _colorView = color.CreateImageView(VkImageViewType._2D, color.GetFullRange(VkImageAspectFlags.ColorBit), null);

        _depth = depth;
        _depthView = depth.CreateImageView(VkImageViewType._2D, depth.GetFullRange(VkImageAspectFlags.DepthBit), null);

        var attachments = stackalloc VkImageView[2];

        attachments[0] = _colorView.VkImageView;
        attachments[1] = _depthView.VkImageView;

        Framebuffer = _device.CreateFramebuffer(new VkFramebufferCreateInfo {

            RenderPass = RenderPass,
            AttachmentCount = 2,
            Attachments = attachments,
            Width = extent.Width,
            Height = extent.Height,
            Layers = 1,

        }, null);

    }

    private ImageEx CreateImage(string name, VkFormat format, VkImageUsageFlags usage) {

        return _device.CreateImage(new ImageEx.CreateInfo {

            Name = name,
            ImageType = VkImageType._2D,
            ImageFormat = format,
            ImageExtent = new VkExtent3D { Width = Extent.Width, Height = Extent.Height, Depth = 1 },
            ImageMipLevels = 1,
            ImageArrayLayers = 1,
            ImageSamples = VkSampleCountFlags._1Bit,
            ImageTiling = VkImageTiling.Optimal,
            ImageUsage = usage,
            AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,

        });

    }

    private unsafe VkRenderPass CreateRenderPass() {

        var attachments = stackalloc VkAttachmentDescription[2];

        attachments[0] = new VkAttachmentDescription {

            Format = ColorFormat,
            Samples = VkSampleCountFlags._1Bit,
            LoadOp = VkAttachmentLoadOp.Clear,
            StoreOp = VkAttachmentStoreOp.Store,
            StencilLoadOp = VkAttachmentLoadOp.DontCare,
            StencilStoreOp = VkAttachmentStoreOp.DontCare,
            InitialLayout = VkImageLayout.Undefined,
            FinalLayout = VkImageLayout.ShaderReadOnlyOptimal,

        };

        attachments[1] = new VkAttachmentDescription {

            Format = DepthFormat,
            Samples = VkSampleCountFlags._1Bit,
            LoadOp = VkAttachmentLoadOp.Clear,
            StoreOp = VkAttachmentStoreOp.DontCare,
            StencilLoadOp = VkAttachmentLoadOp.DontCare,
            StencilStoreOp = VkAttachmentStoreOp.DontCare,
            InitialLayout = VkImageLayout.Undefined,
            FinalLayout = VkImageLayout.DepthStencilAttachmentOptimal,

        };

        var colorRef = new VkAttachmentReference { Attachment = 0, Layout = VkImageLayout.ColorAttachmentOptimal };
        var depthRef = new VkAttachmentReference { Attachment = 1, Layout = VkImageLayout.DepthStencilAttachmentOptimal };

        var subpass = new VkSubpassDescription {

            PipelineBindPoint = VkPipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            ColorAttachments = &colorRef,
            DepthStencilAttachment = &depthRef,

        };

        var dependencies = stackalloc VkSubpassDependency[2];

        // The previous frame's tonemap must finish sampling before this frame overwrites the image.
        dependencies[0] = new VkSubpassDependency {

            SrcSubpass = VK.SUBPASS_EXTERNAL,
            DstSubpass = 0,
            SrcStageMask = VkPipelineStageFlags.FragmentShaderBit | VkPipelineStageFlags.EarlyFragmentTestsBit | VkPipelineStageFlags.LateFragmentTestsBit,
            DstStageMask = VkPipelineStageFlags.ColorAttachmentOutputBit | VkPipelineStageFlags.EarlyFragmentTestsBit | VkPipelineStageFlags.LateFragmentTestsBit,
            SrcAccessMask = VkAccessFlags.DepthStencilAttachmentWriteBit,
            DstAccessMask = VkAccessFlags.ColorAttachmentWriteBit | VkAccessFlags.DepthStencilAttachmentWriteBit | VkAccessFlags.DepthStencilAttachmentReadBit,

        };

        dependencies[1] = new VkSubpassDependency {

            SrcSubpass = 0,
            DstSubpass = VK.SUBPASS_EXTERNAL,
            SrcStageMask = VkPipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = VkPipelineStageFlags.FragmentShaderBit,
            SrcAccessMask = VkAccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = VkAccessFlags.ShaderReadBit,

        };

        return _device.CreateRenderPass(new VkRenderPassCreateInfo {

            AttachmentCount = 2,
            Attachments = attachments,
            SubpassCount = 1,
            Subpasses = &subpass,
            DependencyCount = 2,
            Dependencies = dependencies,

        }, null);

    }

    private void DestroyTargets() {

        _device.DestroyFramebuffer(Framebuffer, null);

        _colorView.Dispose();
        _color?.Dispose();
        _depthView.Dispose();
        _depth?.Dispose();

        _color = null;
        _depth = null;

    }

    public void Dispose() {

        DestroyTargets();

        _device.DestroyRenderPass(RenderPass, null);

    }

}
