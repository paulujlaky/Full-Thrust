using System.Runtime.InteropServices;
using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

// Maps the linear HDR target to the swapchain; exposure is already applied by the scene shaders.
public sealed class TonemapPass : IRenderPass, IResizeListener {

    [StructLayout(LayoutKind.Sequential)]
    private struct PushData {

        public int Tonemapper;
        public float WhitePoint;
        public float BloomIntensity;

    }

    private readonly DeviceEx _device;
    private readonly HdrTarget _target;
    private readonly SceneLighting _lighting;
    private readonly BloomPass _bloom;

    private readonly VkSampler _sampler;
    private readonly VkDescriptorSetLayout _setLayout;
    private readonly VkDescriptorPool _pool;
    private readonly VkDescriptorSet _set;

    private readonly VkPipelineLayout _layout;
    private readonly VkPipeline _pipeline;

    public TonemapPass(DeviceEx device, VkRenderPass renderPass, ShaderLibrary shaders, HdrTarget target, SceneLighting lighting, BloomPass bloom) {

        _device = device;
        _target = target;
        _lighting = lighting;
        _bloom = bloom;

        _sampler = device.CreateSampler(new VkSamplerCreateInfo {

            MagFilter = VkFilter.Nearest,
            MinFilter = VkFilter.Nearest,
            MipmapMode = VkSamplerMipmapMode.Nearest,
            AddressModeU = VkSamplerAddressMode.ClampToEdge,
            AddressModeV = VkSamplerAddressMode.ClampToEdge,
            AddressModeW = VkSamplerAddressMode.ClampToEdge,

        }, null);

        _setLayout = Descriptors.CreateLayout(device, [

            Descriptors.Binding(0, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.FragmentBit),
            Descriptors.Binding(1, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.FragmentBit),

        ]);

        _pool = Descriptors.CreatePool(device, 1, [new VkDescriptorPoolSize { Type = VkDescriptorType.CombinedImageSampler, DescriptorCount = 2 }]);
        _set = Descriptors.Allocate(device, _pool, _setLayout);

        OnResize(target.Extent);

        _layout = device.CreatePipelineLayout([_setLayout], [

            new VkPushConstantRange { StageFlags = VkShaderStageFlags.FragmentBit, Offset = ByteSize.Zero, Size = ByteSize.Of<PushData>() },

        ], null);

        _pipeline = Pipelines.Create(device, shaders, new PipelineDesc {

            RenderPass = renderPass,
            Layout = _layout,
            VertexShader = "fullscreen.vert",
            FragmentShader = "tonemap.frag",
            Cull = VkCullModeFlags.None,
            Depth = DepthMode.None,

        });

    }

    public string Name => "Tonemap";

    public RenderStage Stage => RenderStage.Post;

    public bool Enabled { get; set; } = true;

    public float WhitePoint { get; set; } = 8.0f;

    public void OnResize(VkExtent2D extent) {

        Descriptors.WriteImage(_device, _set, 0, _target.ColorView, _sampler);
        Descriptors.WriteImage(_device, _set, 1, _bloom.View, _bloom.Sampler, VkImageLayout.General);

    }

    public void Record(in FrameContext frame) {

        var cmd = frame.CommandBuffer;

        cmd.BindPipeline(VkPipelineBindPoint.Graphics, _pipeline);
        cmd.BindDescriptorSet(VkPipelineBindPoint.Graphics, _layout, 0, _set, []);
        cmd.PushConstants(_layout, VkShaderStageFlags.FragmentBit, ByteSize.Zero, new PushData { Tonemapper = (int)_lighting.Tonemapper, WhitePoint = WhitePoint, BloomIntensity = _bloom.Intensity });
        cmd.Draw(3, 1, 0, 0);

    }

    public void Dispose() {

        _device.DestroyPipeline(_pipeline, null);
        _device.DestroyPipelineLayout(_layout, null);
        _device.DestroyDescriptorPool(_pool, null);
        _device.DestroyDescriptorSetLayout(_setLayout, null);
        _device.DestroySampler(_sampler, null);

    }

}
