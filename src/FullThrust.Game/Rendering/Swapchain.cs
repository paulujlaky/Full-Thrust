using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

public sealed class Swapchain : IDisposable {

    public const VkFormat DepthFormat = VkFormat.D32SFloat;

    private readonly VulkanContext _context;

    private VkImage[] _images = [];
    private VkImageView[] _views = [];
    private VkFramebuffer[] _framebuffers = [];

    private ImageEx? _depth;
    private ImageViewEx _depthView;

    public Swapchain(VulkanContext context, int2 framebufferSize, bool vsync) {

        _context = context;

        SurfaceFormat = ChooseSurfaceFormat();
        RenderPass = CreateRenderPass();

        Create(framebufferSize, vsync);

    }

    public VkSwapchainKHR Handle { get; private set; }

    public VkSurfaceFormatKHR SurfaceFormat { get; }

    public VkRenderPass RenderPass { get; }

    public VkExtent2D Extent { get; private set; }

    public VkPresentModeKHR PresentMode { get; private set; }

    public bool VSync { get; private set; }

    public int ImageCount => _images.Length;

    public int MinImageCount { get; private set; }

    public VkFramebuffer Framebuffer(int imageIndex) => _framebuffers[imageIndex];

    public VkImage Image(int imageIndex) => _images[imageIndex];

    public bool SupportsCapture { get; private set; }

    public void Recreate(int2 framebufferSize, bool vsync) {

        _context.Device.WaitIdle();

        DestroyTargets();
        Create(framebufferSize, vsync);

    }

    private void Create(int2 framebufferSize, bool vsync) {

        var device = _context.Device;
        var physical = _context.PhysicalDevice;

        var capabilities = physical.GetSurfaceCapabilitiesKHR(_context.Surface);

        Extent = capabilities.CurrentExtent.Width != -1
            ? capabilities.CurrentExtent
            : new VkExtent2D(
                Math.Clamp(framebufferSize.X, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
                Math.Clamp(framebufferSize.Y, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height));

        VSync = vsync;
        PresentMode = ChoosePresentMode(vsync);

        var imageCount = capabilities.MinImageCount + 1;

        if (capabilities.MaxImageCount > 0) {

            imageCount = Math.Min(imageCount, capabilities.MaxImageCount);

        }

        MinImageCount = Math.Max(2, capabilities.MinImageCount);

        SupportsCapture = capabilities.SupportedUsageFlags.HasFlag(VkImageUsageFlags.TransferSrcBit);

        var old = Handle;

        Handle = device.CreateSwapchainKHR(new VkSwapchainCreateInfoKHR {

            Surface = _context.Surface,
            MinImageCount = imageCount,
            ImageFormat = SurfaceFormat.Format,
            ImageColorSpace = SurfaceFormat.ColorSpace,
            ImageExtent = Extent,
            ImageArrayLayers = 1,
            ImageUsage = VkImageUsageFlags.ColorAttachmentBit | (SupportsCapture ? VkImageUsageFlags.TransferSrcBit : VkImageUsageFlags.None),
            ImageSharingMode = VkSharingMode.Exclusive,

            PreTransform = capabilities.SupportedTransforms.HasFlag(VkSurfaceTransformFlagsKHR.IdentityBitKHR)
                ? VkSurfaceTransformFlagsKHR.IdentityBitKHR
                : capabilities.CurrentTransform,

            CompositeAlpha = VkCompositeAlphaFlagsKHR.OpaqueBitKHR,
            PresentMode = PresentMode,
            Clipped = true,
            OldSwapchain = old,

        }, null);

        if (!old.IsNull()) {

            device.DestroySwapchainKHR(old, null);

        }

        _images = new VkImage[device.GetSwapchainImagesKHR_Count(Handle)];
        device.GetSwapchainImagesKHR_Write(Handle, _images);

        _views = new VkImageView[_images.Length];

        for (var i = 0; i < _images.Length; i++) {

            _views[i] = device.CreateImageView(new VkImageViewCreateInfo {

                Image = _images[i],
                ViewType = VkImageViewType._2D,
                Format = SurfaceFormat.Format,

                SubresourceRange = new VkImageSubresourceRange {

                    AspectMask = VkImageAspectFlags.ColorBit,
                    LevelCount = 1,
                    LayerCount = 1,

                },

            }, null);

        }

        var depth = device.CreateImage(new ImageEx.CreateInfo {

            Name = "Depth",
            ImageType = VkImageType._2D,
            ImageFormat = DepthFormat,
            ImageExtent = new VkExtent3D { Width = Extent.Width, Height = Extent.Height, Depth = 1 },
            ImageMipLevels = 1,
            ImageArrayLayers = 1,
            ImageSamples = VkSampleCountFlags._1Bit,
            ImageTiling = VkImageTiling.Optimal,
            ImageUsage = VkImageUsageFlags.DepthStencilAttachmentBit,
            AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,

        });

        _depth = depth;
        _depthView = depth.CreateImageView(VkImageViewType._2D, depth.GetFullRange(VkImageAspectFlags.DepthBit), null);

        _framebuffers = new VkFramebuffer[_images.Length];

        Span<VkImageView> attachments = stackalloc VkImageView[2];

        for (var i = 0; i < _images.Length; i++) {

            attachments[0] = _views[i];
            attachments[1] = _depthView.VkImageView;

            unsafe {

                fixed (VkImageView* pAttachments = attachments) {

                    _framebuffers[i] = device.CreateFramebuffer(new VkFramebufferCreateInfo {

                        RenderPass = RenderPass,
                        AttachmentCount = 2,
                        Attachments = pAttachments,
                        Width = Extent.Width,
                        Height = Extent.Height,
                        Layers = 1,

                    }, null);

                }

            }

        }

    }

    private VkSurfaceFormatKHR ChooseSurfaceFormat() {

        var physical = _context.PhysicalDevice;
        var count = physical.GetSurfaceFormatsKHR_Count(_context.Surface);

        Span<VkSurfaceFormatKHR> formats = stackalloc VkSurfaceFormatKHR[count];

        physical.GetSurfaceFormatsKHR_Write(_context.Surface, formats);

        foreach (var format in formats) {

            if (format.Format == VkFormat.B8G8R8A8SRGB && format.ColorSpace == VkColorSpaceKHR.SRGBNonlinearKHR) {

                return format;

            }

        }

        return formats[0];

    }

    private VkPresentModeKHR ChoosePresentMode(bool vsync) {

        if (vsync) {

            return VkPresentModeKHR.FifoKHR;

        }

        var physical = _context.PhysicalDevice;
        var count = physical.GetSurfacePresentModesKHR_Count(_context.Surface);

        Span<VkPresentModeKHR> modes = stackalloc VkPresentModeKHR[count];

        physical.GetSurfacePresentModesKHR_Write(_context.Surface, modes);

        var fallback = VkPresentModeKHR.FifoKHR;

        foreach (var mode in modes) {

            if (mode == VkPresentModeKHR.MailboxKHR) {

                return mode;

            }

            if (mode == VkPresentModeKHR.ImmediateKHR) {

                fallback = mode;

            }

        }

        return fallback;

    }

    private unsafe VkRenderPass CreateRenderPass() {

        var attachments = stackalloc VkAttachmentDescription[2];

        attachments[0] = new VkAttachmentDescription {

            Format = SurfaceFormat.Format,
            Samples = VkSampleCountFlags._1Bit,
            LoadOp = VkAttachmentLoadOp.Clear,
            StoreOp = VkAttachmentStoreOp.Store,
            StencilLoadOp = VkAttachmentLoadOp.DontCare,
            StencilStoreOp = VkAttachmentStoreOp.DontCare,
            InitialLayout = VkImageLayout.Undefined,
            FinalLayout = VkImageLayout.PresentSrcKHR,

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

        var dependency = new VkSubpassDependency {

            SrcSubpass = VK.SUBPASS_EXTERNAL,
            DstSubpass = 0,
            SrcStageMask = VkPipelineStageFlags.ColorAttachmentOutputBit | VkPipelineStageFlags.EarlyFragmentTestsBit | VkPipelineStageFlags.LateFragmentTestsBit,
            DstStageMask = VkPipelineStageFlags.ColorAttachmentOutputBit | VkPipelineStageFlags.EarlyFragmentTestsBit | VkPipelineStageFlags.LateFragmentTestsBit,
            SrcAccessMask = VkAccessFlags.DepthStencilAttachmentWriteBit,
            DstAccessMask = VkAccessFlags.ColorAttachmentWriteBit | VkAccessFlags.DepthStencilAttachmentWriteBit | VkAccessFlags.DepthStencilAttachmentReadBit,

        };

        return _context.Device.CreateRenderPass(new VkRenderPassCreateInfo {

            AttachmentCount = 2,
            Attachments = attachments,
            SubpassCount = 1,
            Subpasses = &subpass,
            DependencyCount = 1,
            Dependencies = &dependency,

        }, null);

    }

    private void DestroyTargets() {

        var device = _context.Device;

        foreach (var framebuffer in _framebuffers) {

            device.DestroyFramebuffer(framebuffer, null);

        }

        foreach (var view in _views) {

            device.DestroyImageView(view, null);

        }

        if (!_depthView.IsNull()) {

            _depthView.Dispose();

        }
        _depth?.Dispose();

        _framebuffers = [];
        _views = [];
        _depthView = default;
        _depth = null;

    }

    public void Dispose() {

        DestroyTargets();

        _context.Device.DestroySwapchainKHR(Handle, null);
        _context.Device.DestroyRenderPass(RenderPass, null);

    }

}
