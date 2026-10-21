using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct BackgroundPush {

    public float4 Params;

}

// Stars, the Milky Way and the solar disc, drawn at the far plane wherever no geometry was.
public sealed class BackgroundPass : IRenderPass {

    private readonly DeviceEx _device;

    private readonly VkPipelineLayout _layout;
    private readonly VkPipeline _pipeline;

    public BackgroundPass(DeviceEx device, VkRenderPass renderPass, VkDescriptorSetLayout frameLayout, ShaderLibrary shaders) {

        _device = device;

        _layout = device.CreatePipelineLayout([frameLayout], [

            new VkPushConstantRange { StageFlags = VkShaderStageFlags.FragmentBit, Offset = ByteSize.Zero, Size = ByteSize.Of<BackgroundPush>() },

        ], null);

        // The fullscreen triangle sits at depth 0, the reverse-Z far plane, so it only passes where depth was never written.
        _pipeline = Pipelines.Create(device, shaders, new PipelineDesc {

            RenderPass = renderPass,
            Layout = _layout,
            VertexShader = "fullscreen.vert",
            FragmentShader = "background.frag",
            Cull = VkCullModeFlags.None,
            Depth = DepthMode.TestOnly,

        });

    }

    public string Name => "Background";

    public RenderStage Stage => RenderStage.Opaque;

    public bool Enabled { get; set; } = true;

    // 1 is physical: stars are then invisible at daylight exposures, as in real photographs.
    public float StarBrightness { get; set; } = 1.0f;

    public float MilkyWayBrightness { get; set; } = 1.0f;

    public float FaintestMagnitude { get; set; } = 10.0f;

    public void Record(in FrameContext frame) {

        var cmd = frame.CommandBuffer;

        // Vertical field of view from the projection, spread over the image height.
        var pixel = 2.0f / (MathF.Abs(frame.Projection.Y.Y) * frame.Extent.Height);

        cmd.BindPipeline(VkPipelineBindPoint.Graphics, _pipeline);
        cmd.BindDescriptorSet(VkPipelineBindPoint.Graphics, _layout, 0, frame.FrameSet, []);
        cmd.PushConstants(_layout, VkShaderStageFlags.FragmentBit, ByteSize.Zero, new BackgroundPush { Params = new float4(StarBrightness, MilkyWayBrightness, pixel, FaintestMagnitude) });
        cmd.Draw(3, 1, 0, 0);

    }

    public void Dispose() {

        _device.DestroyPipeline(_pipeline, null);
        _device.DestroyPipelineLayout(_layout, null);

    }

}
