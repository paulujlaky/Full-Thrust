using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

// A compute-written, shader-sampled image kept in the General layout.
public sealed class RenderTexture : IDisposable {

    private readonly DeviceEx _device;

    private readonly string _name;
    private readonly VkFormat _format;
    private readonly VkImageUsageFlags _usage;

    private ImageEx? _image;
    private ImageViewEx _view;

    private bool _ready;

    public RenderTexture(DeviceEx device, string name, VkFormat format, int width, int height, int depth = 1, VkImageUsageFlags extraUsage = VkImageUsageFlags.None) {

        _device = device;
        _name = name;
        _format = format;
        _usage = VkImageUsageFlags.StorageBit | VkImageUsageFlags.SampledBit | extraUsage;

        Create(width, height, depth);

    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int Depth { get; private set; }

    public VkImageView View => _view.VkImageView;

    public void Resize(int width, int height, int depth = 1) {

        if (width == Width && height == Height && depth == Depth) {

            return;

        }

        Destroy();
        Create(width, height, depth);

    }

    // Moves a fresh image into the General layout; does nothing afterwards.
    public void Prepare(CommandBuffer cmd) {

        if (_ready) {

            return;

        }

        cmd.PipelineBarrier(VkPipelineStageFlags.TopOfPipeBit, VkPipelineStageFlags.ComputeShaderBit | VkPipelineStageFlags.FragmentShaderBit, VkDependencyFlags.None, [], [], [

            new VkImageMemoryBarrier {

                SrcAccessMask = VkAccessFlags.None,
                DstAccessMask = VkAccessFlags.ShaderReadBit | VkAccessFlags.ShaderWriteBit,
                OldLayout = VkImageLayout.Undefined,
                NewLayout = VkImageLayout.General,
                SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                Image = _image!.Value.VkImage,
                SubresourceRange = new VkImageSubresourceRange { AspectMask = VkImageAspectFlags.ColorBit, LevelCount = 1, LayerCount = 1 },

            },

        ]);

        _ready = true;

    }

    private void Create(int width, int height, int depth) {

        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Depth = Math.Max(1, depth);

        var volume = Depth > 1;

        var image = _device.CreateImage(new ImageEx.CreateInfo {

            Name = _name,
            ImageType = volume ? VkImageType._3D : VkImageType._2D,
            ImageFormat = _format,
            ImageExtent = new VkExtent3D { Width = Width, Height = Height, Depth = Depth },
            ImageMipLevels = 1,
            ImageArrayLayers = 1,
            ImageSamples = VkSampleCountFlags._1Bit,
            ImageTiling = VkImageTiling.Optimal,
            ImageUsage = _usage,
            AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,

        });

        _image = image;
        _view = image.CreateImageView(volume ? VkImageViewType._3D : VkImageViewType._2D, image.GetFullRange(VkImageAspectFlags.ColorBit), null);
        _ready = false;

    }

    private void Destroy() {

        _view.Dispose();
        _image?.Dispose();
        _image = null;

    }

    public void Dispose() => Destroy();

}
