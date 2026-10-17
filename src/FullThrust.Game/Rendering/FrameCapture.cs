using Brutal;
using Brutal.Logging;
using Brutal.StbApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

// Copies a presented swapchain image to a PNG. Capturing stalls the GPU, so it is for tooling only.
public sealed class FrameCapture : IDisposable {

    private static readonly LogCategory Log = LogCategory.Make("Capture");

    private readonly VulkanContext _context;

    private BufferEx _readback;
    private bool _hasReadback;
    private nuint _readbackBytes;

    private string? _pendingPath;
    private string? _recordedPath;

    private VkExtent2D _recordedExtent;
    private VkFormat _recordedFormat;

    public FrameCapture(VulkanContext context) {

        _context = context;

    }

    public bool IsPending => _pendingPath is not null;

    public int CapturesWritten { get; private set; }

    public void Request(string path) => _pendingPath = path;

    public void Record(CommandBuffer cmd, VkImage image, VkExtent2D extent, VkFormat format) {

        if (_pendingPath is null) {

            return;

        }

        var size = ByteSize.Of<uint>(extent.Width * extent.Height);

        if (!_hasReadback || _readbackBytes < size.Bytes) {

            if (_hasReadback) {

                _readback.Dispose();

            }

            _readbackBytes = size.Bytes;
            _hasReadback = true;

            _readback = _context.Device.CreateBuffer(new BufferEx.CreateInfo {

                Name = "CaptureReadback",
                BufferSize = size,
                BufferUsage = VkBufferUsageFlags.TransferDstBit,
                AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,

            });

        }

        var range = new VkImageSubresourceRange { AspectMask = VkImageAspectFlags.ColorBit, LevelCount = 1, LayerCount = 1 };

        cmd.PipelineBarrier(VkPipelineStageFlags.ColorAttachmentOutputBit, VkPipelineStageFlags.TransferBit, VkDependencyFlags.None, [], [], [

            new VkImageMemoryBarrier {

                SrcAccessMask = VkAccessFlags.ColorAttachmentWriteBit,
                DstAccessMask = VkAccessFlags.TransferReadBit,
                OldLayout = VkImageLayout.PresentSrcKHR,
                NewLayout = VkImageLayout.TransferSrcOptimal,
                SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                Image = image,
                SubresourceRange = range,

            },

        ]);

        cmd.CopyImageToBuffer(image, VkImageLayout.TransferSrcOptimal, _readback.VkBuffer, [

            new VkBufferImageCopy {

                ImageSubresource = new VkImageSubresourceLayers { AspectMask = VkImageAspectFlags.ColorBit, LayerCount = 1 },
                ImageExtent = new VkExtent3D { Width = extent.Width, Height = extent.Height, Depth = 1 },

            },

        ]);

        cmd.PipelineBarrier(VkPipelineStageFlags.TransferBit, VkPipelineStageFlags.BottomOfPipeBit, VkDependencyFlags.None, [], [], [

            new VkImageMemoryBarrier {

                SrcAccessMask = VkAccessFlags.TransferReadBit,
                DstAccessMask = VkAccessFlags.None,
                OldLayout = VkImageLayout.TransferSrcOptimal,
                NewLayout = VkImageLayout.PresentSrcKHR,
                SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                Image = image,
                SubresourceRange = range,

            },

        ]);

        _recordedPath = _pendingPath;
        _recordedExtent = extent;
        _recordedFormat = format;
        _pendingPath = null;

    }

    public void CompleteAfterSubmit() {

        if (_recordedPath is null || !_hasReadback) {

            return;

        }

        _context.GraphicsQueue.WaitIdle();

        var width = _recordedExtent.Width;
        var height = _recordedExtent.Height;
        var pixels = new byte[width * height * 4];

        using (var mapped = _readback.Map()) {

            mapped.AsSpan<byte>()[..pixels.Length].CopyTo(pixels);

        }

        var swapRedBlue = _recordedFormat is VkFormat.B8G8R8A8SRGB or VkFormat.B8G8R8A8UNorm;

        for (var i = 0; i < pixels.Length; i += 4) {

            if (swapRedBlue) {

                (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);

            }

            pixels[i + 3] = 255;

        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(_recordedPath));

        if (!string.IsNullOrEmpty(directory)) {

            Directory.CreateDirectory(directory);

        }

        if (Stb.WritePng(_recordedPath, width, height, 4, pixels)) {

            CapturesWritten++;
            Log.Info($"Captured {width}x{height} frame to {_recordedPath}");

        } else {

            Log.Error($"Failed to write capture {_recordedPath}");

        }

        _recordedPath = null;

    }

    public void Dispose() {

        if (_hasReadback) {

            _readback.Dispose();

        }

    }

}
