using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

public enum DepthMode {

    None,
    TestOnly,
    TestWrite,

}

public unsafe struct PipelineDesc {

    public VkRenderPass RenderPass;
    public VkPipelineLayout Layout;

    public string VertexShader;

    // Null builds a depth-only pipeline.
    public string? FragmentShader;

    // Null draws without vertex buffers, e.g. a fullscreen triangle.
    public VkPipelineVertexInputStateCreateInfo* VertexInput;

    public VkPipelineInputAssemblyStateCreateInfo* InputAssembly;
    public VkCullModeFlags Cull;
    public VkPipelineColorBlendStateCreateInfo* Blend;

    public DepthMode Depth;

    // Shadow casters: depth bias is set per pass, and geometry outside the depth range is clamped rather than clipped.
    public bool ShadowCaster;

}

public static class Pipelines {

    // Reverse-Z everywhere: nearer fragments have larger depth.
    public static unsafe VkPipeline Create(DeviceEx device, ShaderLibrary shaders, in PipelineDesc desc) {

        var vertexShader = shaders.Load(desc.VertexShader);
        var fragmentShader = desc.FragmentShader is null ? default : shaders.Load(desc.FragmentShader);

        try {

            var stages = stackalloc VkPipelineShaderStageCreateInfo[2];

            stages[0] = Presets.ShaderStage.Vertex(vertexShader);

            if (desc.FragmentShader is not null) {

                stages[1] = Presets.ShaderStage.Fragment(fragmentShader);

            }

            var emptyInput = new VkPipelineVertexInputStateCreateInfo();
            var noColor = new VkPipelineColorBlendStateCreateInfo();

            // glTF and every generated mesh wind front faces counter-clockwise; the projection flips Y to match.
            var rasterization = new VkPipelineRasterizationStateCreateInfo {

                PolygonMode = VkPolygonMode.Fill,
                CullMode = desc.Cull,
                FrontFace = VkFrontFace.CounterClockwise,
                LineWidth = 1,
                DepthClampEnable = desc.ShadowCaster,
                DepthBiasEnable = desc.ShadowCaster,

            };

            var viewport = new VkPipelineViewportStateCreateInfo { ViewportCount = 1, ScissorCount = 1 };

            var dynamicStates = stackalloc VkDynamicState[3];

            dynamicStates[0] = VkDynamicState.Viewport;
            dynamicStates[1] = VkDynamicState.Scissor;
            dynamicStates[2] = VkDynamicState.DepthBias;

            var dynamic = new VkPipelineDynamicStateCreateInfo { DynamicStateCount = desc.ShadowCaster ? 3 : 2, DynamicStates = dynamicStates };

            var depth = new VkPipelineDepthStencilStateCreateInfo {

                DepthTestEnable = desc.Depth != DepthMode.None,
                DepthWriteEnable = desc.Depth == DepthMode.TestWrite,
                DepthCompareOp = VkCompareOp.GreaterOrEqual,

            };

            return device.CreateGraphicsPipeline(default, new VkGraphicsPipelineCreateInfo {

                StageCount = desc.FragmentShader is null ? 1 : 2,
                Stages = stages,
                VertexInputState = desc.VertexInput is null ? &emptyInput : desc.VertexInput,
                InputAssemblyState = desc.InputAssembly is null ? Presets.InputAssembly.TriangleList : desc.InputAssembly,
                ViewportState = &viewport,
                RasterizationState = &rasterization,
                MultisampleState = Presets.Multisample.None,
                DepthStencilState = &depth,
                ColorBlendState = desc.FragmentShader is null ? &noColor : desc.Blend is null ? Presets.BlendState.BlendNone : desc.Blend,
                DynamicState = &dynamic,
                Layout = desc.Layout,
                RenderPass = desc.RenderPass,
                Subpass = 0,

            }, null);

        } finally {

            device.DestroyShaderModule(vertexShader, null);

            if (desc.FragmentShader is not null) {

                device.DestroyShaderModule(fragmentShader, null);

            }

        }

    }

}
