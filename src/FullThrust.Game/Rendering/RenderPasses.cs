using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

public struct AttachmentDesc {

    public VkFormat Format;

    public VkAttachmentLoadOp Load;
    public VkAttachmentStoreOp Store;

    public VkImageLayout Initial;
    public VkImageLayout Subpass;
    public VkImageLayout Final;

}

// Single-subpass render passes with broad external dependencies, so stages can be reordered freely.
public static class RenderPasses {

    private const VkPipelineStageFlags Stages = VkPipelineStageFlags.ComputeShaderBit | VkPipelineStageFlags.FragmentShaderBit | VkPipelineStageFlags.EarlyFragmentTestsBit | VkPipelineStageFlags.LateFragmentTestsBit | VkPipelineStageFlags.ColorAttachmentOutputBit | VkPipelineStageFlags.TransferBit;

    private const VkAccessFlags Writes = VkAccessFlags.ColorAttachmentWriteBit | VkAccessFlags.DepthStencilAttachmentWriteBit | VkAccessFlags.ShaderWriteBit | VkAccessFlags.TransferWriteBit;

    private const VkAccessFlags Access = Writes | VkAccessFlags.ColorAttachmentReadBit | VkAccessFlags.DepthStencilAttachmentReadBit | VkAccessFlags.ShaderReadBit | VkAccessFlags.TransferReadBit;

    public static unsafe VkRenderPass Create(DeviceEx device, AttachmentDesc? color, AttachmentDesc? depth) {

        var attachments = stackalloc VkAttachmentDescription[2];
        var count = 0;

        VkAttachmentReference colorRef = default;
        VkAttachmentReference depthRef = default;

        if (color is { } c) {

            colorRef = new VkAttachmentReference { Attachment = count, Layout = c.Subpass };
            attachments[count++] = Describe(c);

        }

        if (depth is { } d) {

            depthRef = new VkAttachmentReference { Attachment = count, Layout = d.Subpass };
            attachments[count++] = Describe(d);

        }

        var subpass = new VkSubpassDescription {

            PipelineBindPoint = VkPipelineBindPoint.Graphics,
            ColorAttachmentCount = color is null ? 0 : 1,
            ColorAttachments = color is null ? null : &colorRef,
            DepthStencilAttachment = depth is null ? null : &depthRef,

        };

        var dependencies = stackalloc VkSubpassDependency[2];

        dependencies[0] = new VkSubpassDependency {

            SrcSubpass = VK.SUBPASS_EXTERNAL,
            DstSubpass = 0,
            SrcStageMask = Stages,
            DstStageMask = Stages,
            SrcAccessMask = Writes,
            DstAccessMask = Access,

        };

        dependencies[1] = new VkSubpassDependency {

            SrcSubpass = 0,
            DstSubpass = VK.SUBPASS_EXTERNAL,
            SrcStageMask = Stages,
            DstStageMask = Stages,
            SrcAccessMask = Writes,
            DstAccessMask = Access,

        };

        return device.CreateRenderPass(new VkRenderPassCreateInfo {

            AttachmentCount = count,
            Attachments = attachments,
            SubpassCount = 1,
            Subpasses = &subpass,
            DependencyCount = 2,
            Dependencies = dependencies,

        }, null);

    }

    private static VkAttachmentDescription Describe(in AttachmentDesc a) {

        return new VkAttachmentDescription {

            Format = a.Format,
            Samples = VkSampleCountFlags._1Bit,
            LoadOp = a.Load,
            StoreOp = a.Store,
            StencilLoadOp = VkAttachmentLoadOp.DontCare,
            StencilStoreOp = VkAttachmentStoreOp.DontCare,
            InitialLayout = a.Initial,
            FinalLayout = a.Final,

        };

    }

}
