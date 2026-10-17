using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

public sealed class DebugLinePass : IRenderPass {

    private readonly DeviceEx _device;
    private readonly DebugLines _lines;

    private readonly VkPipelineLayout _layout;
    private readonly VkPipeline _pipeline;

    // One buffer per frame in flight so the CPU never writes vertices the GPU is still reading.
    private readonly BufferEx[] _buffers = new BufferEx[Renderer.FramesInFlight];
    private readonly MappedMemory[] _mapped = new MappedMemory[Renderer.FramesInFlight];

    public DebugLinePass(DeviceEx device, VkRenderPass renderPass, ShaderLibrary shaders, DebugLines lines) {

        _device = device;
        _lines = lines;

        for (var i = 0; i < _buffers.Length; i++) {

            _buffers[i] = device.CreateBuffer(new BufferEx.CreateInfo {

                Name = "DebugLines",
                BufferSize = ByteSize.Of<LineVertex>(lines.Capacity),
                BufferUsage = VkBufferUsageFlags.VertexBufferBit,
                AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,

            });

            _mapped[i] = _buffers[i].Map();

        }

        _layout = device.CreatePipelineLayout([], [

            new VkPushConstantRange { StageFlags = VkShaderStageFlags.VertexBit, Offset = ByteSize.Zero, Size = ByteSize.Of<Brutal.Numerics.float4x4>() },

        ], null);

        var vertexShader = shaders.Load("debug_line.vert");
        var fragmentShader = shaders.Load("debug_line.frag");

        try {

            _pipeline = CreatePipeline(renderPass, vertexShader, fragmentShader);

        } finally {

            device.DestroyShaderModule(vertexShader, null);
            device.DestroyShaderModule(fragmentShader, null);

        }

    }

    public string Name => "Debug Lines";

    public bool Enabled { get; set; } = true;

    public void Record(in FrameContext frame) {

        var vertices = _lines.Vertices;

        if (vertices.Length == 0) {

            return;

        }

        vertices.CopyTo(_mapped[frame.FrameIndex].AsSpan<LineVertex>());

        var cmd = frame.CommandBuffer;

        cmd.BindPipeline(VkPipelineBindPoint.Graphics, _pipeline);
        cmd.BindVertexBuffer(0, _buffers[frame.FrameIndex].VkBuffer);
        cmd.PushConstants(_layout, VkShaderStageFlags.VertexBit, ByteSize.Zero, frame.View * frame.Projection);
        cmd.Draw(vertices.Length, 1, 0, 0);

    }

    private unsafe VkPipeline CreatePipeline(VkRenderPass renderPass, VkShaderModule vertexShader, VkShaderModule fragmentShader) {

        var stages = stackalloc VkPipelineShaderStageCreateInfo[2];

        stages[0] = Presets.ShaderStage.Vertex(vertexShader);
        stages[1] = Presets.ShaderStage.Fragment(fragmentShader);

        var vertexInput = VertexInput.For<LineVertex>();

        var viewport = new VkPipelineViewportStateCreateInfo { ViewportCount = 1, ScissorCount = 1 };

        var dynamicStates = stackalloc VkDynamicState[2];

        dynamicStates[0] = VkDynamicState.Viewport;
        dynamicStates[1] = VkDynamicState.Scissor;

        var dynamic = new VkPipelineDynamicStateCreateInfo { DynamicStateCount = 2, DynamicStates = dynamicStates };

        var depth = new VkPipelineDepthStencilStateCreateInfo {

            DepthTestEnable = true,
            DepthWriteEnable = false,
            DepthCompareOp = VkCompareOp.GreaterOrEqual,

        };

        return _device.CreateGraphicsPipeline(default, new VkGraphicsPipelineCreateInfo {

            StageCount = 2,
            Stages = stages,
            VertexInputState = vertexInput,
            InputAssemblyState = Presets.InputAssembly.LineList,
            ViewportState = &viewport,
            RasterizationState = Presets.Rasterization.Fill.CullNone,
            MultisampleState = Presets.Multisample.None,
            DepthStencilState = &depth,
            ColorBlendState = Presets.BlendState.BlendColorAlpha,
            DynamicState = &dynamic,
            Layout = _layout,
            RenderPass = renderPass,
            Subpass = 0,

        }, null);

    }

    public void Dispose() {

        _device.DestroyPipeline(_pipeline, null);
        _device.DestroyPipelineLayout(_layout, null);

        for (var i = 0; i < _buffers.Length; i++) {

            _mapped[i].Dispose();
            _buffers[i].Dispose();

        }

    }

}
