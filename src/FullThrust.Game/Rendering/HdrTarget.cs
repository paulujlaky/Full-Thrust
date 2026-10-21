using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

// Linear HDR colour and depth; between passes both rest in sampleable layouts.
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

        DepthPass = RenderPasses.Create(device, null, new AttachmentDesc {

            Format = DepthFormat,
            Load = VkAttachmentLoadOp.Clear,
            Store = VkAttachmentStoreOp.Store,
            Initial = VkImageLayout.Undefined,
            Subpass = VkImageLayout.DepthStencilAttachmentOptimal,
            Final = VkImageLayout.DepthStencilReadOnlyOptimal,

        });

        OpaquePass = RenderPasses.Create(device, new AttachmentDesc {

            Format = ColorFormat,
            Load = VkAttachmentLoadOp.Clear,
            Store = VkAttachmentStoreOp.Store,
            Initial = VkImageLayout.Undefined,
            Subpass = VkImageLayout.ColorAttachmentOptimal,
            Final = VkImageLayout.ShaderReadOnlyOptimal,

        }, new AttachmentDesc {

            Format = DepthFormat,
            Load = VkAttachmentLoadOp.Load,
            Store = VkAttachmentStoreOp.Store,
            Initial = VkImageLayout.DepthStencilReadOnlyOptimal,
            Subpass = VkImageLayout.DepthStencilAttachmentOptimal,
            Final = VkImageLayout.DepthStencilReadOnlyOptimal,

        });

        TransparentPass = RenderPasses.Create(device, new AttachmentDesc {

            Format = ColorFormat,
            Load = VkAttachmentLoadOp.Load,
            Store = VkAttachmentStoreOp.Store,
            Initial = VkImageLayout.ShaderReadOnlyOptimal,
            Subpass = VkImageLayout.ColorAttachmentOptimal,
            Final = VkImageLayout.ShaderReadOnlyOptimal,

        }, new AttachmentDesc {

            Format = DepthFormat,
            Load = VkAttachmentLoadOp.Load,
            Store = VkAttachmentStoreOp.Store,
            Initial = VkImageLayout.DepthStencilReadOnlyOptimal,
            Subpass = VkImageLayout.DepthStencilReadOnlyOptimal,
            Final = VkImageLayout.DepthStencilReadOnlyOptimal,

        });

        Create(extent);

    }

    // Depth only; pipelines built against it also work in ShadowMaps.RenderPass.
    public VkRenderPass DepthPass { get; }

    // Colour and depth; pipelines built against it also work in TransparentPass.
    public VkRenderPass OpaquePass { get; }

    public VkRenderPass TransparentPass { get; }

    public VkFramebuffer DepthFramebuffer { get; private set; }

    public VkFramebuffer Framebuffer { get; private set; }

    public VkExtent2D Extent { get; private set; }

    public VkImageView ColorView => _colorView.VkImageView;

    public VkImageView DepthView => _depthView.VkImageView;

    public void Resize(VkExtent2D extent) {

        DestroyTargets();
        Create(extent);

    }

    private unsafe void Create(VkExtent2D extent) {

        Extent = extent;

        var color = CreateImage("HdrColor", ColorFormat, VkImageUsageFlags.ColorAttachmentBit | VkImageUsageFlags.SampledBit | VkImageUsageFlags.StorageBit | VkImageUsageFlags.TransferSrcBit);
        var depth = CreateImage("HdrDepth", DepthFormat, VkImageUsageFlags.DepthStencilAttachmentBit | VkImageUsageFlags.SampledBit);

        _color = color;
        _colorView = color.CreateImageView(VkImageViewType._2D, color.GetFullRange(VkImageAspectFlags.ColorBit), null);

        _depth = depth;
        _depthView = depth.CreateImageView(VkImageViewType._2D, depth.GetFullRange(VkImageAspectFlags.DepthBit), null);

        var attachments = stackalloc VkImageView[2];

        attachments[0] = _colorView.VkImageView;
        attachments[1] = _depthView.VkImageView;

        Framebuffer = _device.CreateFramebuffer(new VkFramebufferCreateInfo {

            RenderPass = OpaquePass,
            AttachmentCount = 2,
            Attachments = attachments,
            Width = extent.Width,
            Height = extent.Height,
            Layers = 1,

        }, null);

        DepthFramebuffer = _device.CreateFramebuffer(new VkFramebufferCreateInfo {

            RenderPass = DepthPass,
            AttachmentCount = 1,
            Attachments = attachments + 1,
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

    private void DestroyTargets() {

        _device.DestroyFramebuffer(Framebuffer, null);
        _device.DestroyFramebuffer(DepthFramebuffer, null);

        _colorView.Dispose();
        _color?.Dispose();
        _depthView.Dispose();
        _depth?.Dispose();

        _color = null;
        _depth = null;

    }

    public void Dispose() {

        DestroyTargets();

        _device.DestroyRenderPass(DepthPass, null);
        _device.DestroyRenderPass(OpaquePass, null);
        _device.DestroyRenderPass(TransparentPass, null);

    }

}
