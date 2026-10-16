using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using FullThrust.Core.Ecs;
using FullThrust.Core.Rendering;
using FullThrust.Core.Space;

namespace FullThrust.Game.Rendering;

public sealed class DebugShapePass : IRenderPass {

    [StructLayout(LayoutKind.Sequential)]
    private struct PushData {

        public float4x4 Mvp;

        public float4 Normal0;
        public float4 Normal1;
        public float4 Normal2;

        public float4 Color;

    }

    private const VkShaderStageFlags PushStages = VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit;

    private readonly DeviceEx _device;

    private readonly World _world;
    private readonly FloatingOrigin _origin;

    private readonly VkPipelineLayout _layout;
    private readonly VkPipeline _pipeline;

    private readonly GpuMesh[] _meshes;

    public DebugShapePass(DeviceEx device, VkRenderPass renderPass, ShaderLibrary shaders, World world, FloatingOrigin origin) {

        _device = device;
        _world = world;
        _origin = origin;

        _meshes = new GpuMesh[Enum.GetValues<ShapeKind>().Length];
        _meshes[(int)ShapeKind.Box] = new GpuMesh(device, "DebugBox", MeshData.UnitBox());
        _meshes[(int)ShapeKind.Sphere] = new GpuMesh(device, "DebugSphere", MeshData.UnitSphere(96, 192));

        _layout = device.CreatePipelineLayout([], [

            new VkPushConstantRange { StageFlags = PushStages, Offset = ByteSize.Zero, Size = ByteSize.Of<PushData>() },

        ], null);

        var vertexShader = shaders.Load("debug_shape.vert");
        var fragmentShader = shaders.Load("debug_shape.frag");

        try {

            _pipeline = CreatePipeline(renderPass, vertexShader, fragmentShader);

        } finally {

            device.DestroyShaderModule(vertexShader, null);
            device.DestroyShaderModule(fragmentShader, null);

        }

    }

    public string Name => "Debug Shapes";

    public bool Enabled { get; set; } = true;

    public float3 SunDirection { get; set; } = float3.Normalize(new float3(0.6f, 0.7f, 0.35f));

    public int DrawCount { get; private set; }

    public void Record(in FrameContext frame) {

        var cmd = frame.CommandBuffer;
        var viewProjection = frame.View * frame.Projection;
        var sun = SunDirection;

        cmd.BindPipeline(VkPipelineBindPoint.Graphics, _pipeline);

        var bound = -1;
        var draws = 0;

        foreach (var row in _world.Query<Transform, DebugShape>()) {

            ref readonly var transform = ref row.C1;
            ref readonly var shape = ref row.C2;

            var kind = (int)shape.Kind;

            if (kind != bound) {

                _meshes[kind].Bind(cmd);
                bound = kind;

            }

            var rotation = float4x4.CreateFromQuaternion(SpaceMath.ToFloat(transform.Rotation));
            var model = float4x4.CreateScale(shape.Scale) * rotation * float4x4.CreateTranslation(_origin.ToLocal(transform.Position));

            var push = new PushData {

                Mvp = model * viewProjection,

                Normal0 = new float4(rotation.X.X, rotation.X.Y, rotation.X.Z, sun.X),
                Normal1 = new float4(rotation.Y.X, rotation.Y.Y, rotation.Y.Z, sun.Y),
                Normal2 = new float4(rotation.Z.X, rotation.Z.Y, rotation.Z.Z, sun.Z),

                Color = shape.Color,

            };

            cmd.PushConstants(_layout, PushStages, ByteSize.Zero, push);
            cmd.DrawIndexed(_meshes[kind].IndexCount, 1, 0, 0, 0);

            draws++;

        }

        DrawCount = draws;

    }

    private unsafe VkPipeline CreatePipeline(VkRenderPass renderPass, VkShaderModule vertexShader, VkShaderModule fragmentShader) {

        var stages = stackalloc VkPipelineShaderStageCreateInfo[2];

        stages[0] = Presets.ShaderStage.Vertex(vertexShader);
        stages[1] = Presets.ShaderStage.Fragment(fragmentShader);

        var vertexInput = VertexInput.For<Vertex>();

        var viewport = new VkPipelineViewportStateCreateInfo { ViewportCount = 1, ScissorCount = 1 };

        var dynamicStates = stackalloc VkDynamicState[2];

        dynamicStates[0] = VkDynamicState.Viewport;
        dynamicStates[1] = VkDynamicState.Scissor;

        var dynamic = new VkPipelineDynamicStateCreateInfo { DynamicStateCount = 2, DynamicStates = dynamicStates };

        var depth = new VkPipelineDepthStencilStateCreateInfo {

            DepthTestEnable = true,
            DepthWriteEnable = true,
            DepthCompareOp = VkCompareOp.GreaterOrEqual,

        };

        return _device.CreateGraphicsPipeline(default, new VkGraphicsPipelineCreateInfo {

            StageCount = 2,
            Stages = stages,
            VertexInputState = vertexInput,
            InputAssemblyState = Presets.InputAssembly.TriangleList,
            ViewportState = &viewport,
            RasterizationState = Presets.Rasterization.Fill.CullNone,
            MultisampleState = Presets.Multisample.None,
            DepthStencilState = &depth,
            ColorBlendState = Presets.BlendState.BlendNone,
            DynamicState = &dynamic,
            Layout = _layout,
            RenderPass = renderPass,
            Subpass = 0,

        }, null);

    }

    public void Dispose() {

        _device.DestroyPipeline(_pipeline, null);
        _device.DestroyPipelineLayout(_layout, null);

        foreach (var mesh in _meshes) {

            mesh.Dispose();

        }

    }

}
