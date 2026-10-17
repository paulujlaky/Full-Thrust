using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using FullThrust.Assets;
using FullThrust.Core.Ecs;
using FullThrust.Core.Rendering;
using FullThrust.Core.Space;

namespace FullThrust.Game.Rendering;

// Draws every entity with a ModelInstance using the glTF metallic-roughness materials.
public sealed class MeshPass : IRenderPass {

    private readonly DeviceEx _device;

    private readonly World _world;
    private readonly FloatingOrigin _origin;
    private readonly ModelLibrary _models;

    private readonly VkPipelineLayout _layout;
    private readonly VkPipeline _singleSided;
    private readonly VkPipeline _doubleSided;

    public unsafe MeshPass(DeviceEx device, VkRenderPass renderPass, VkDescriptorSetLayout frameLayout, ShaderLibrary shaders, World world, FloatingOrigin origin, ModelLibrary models) {

        _device = device;
        _world = world;
        _origin = origin;
        _models = models;

        _layout = device.CreatePipelineLayout([frameLayout, models.MaterialLayout], [

            new VkPushConstantRange { StageFlags = VkShaderStageFlags.VertexBit, Offset = ByteSize.Zero, Size = ByteSize.Of<MeshPush>() },

        ], null);

        var desc = new PipelineDesc {

            RenderPass = renderPass,
            Layout = _layout,
            VertexShader = "mesh.vert",
            FragmentShader = "mesh.frag",
            VertexInput = VertexInput.For<MeshVertex>(),
            Cull = VkCullModeFlags.BackBit,
            Depth = DepthMode.TestWrite,

        };

        _singleSided = Pipelines.Create(device, shaders, desc);

        desc.Cull = VkCullModeFlags.None;

        _doubleSided = Pipelines.Create(device, shaders, desc);

    }

    public string Name => "Meshes";

    public RenderStage Stage => RenderStage.Scene;

    public bool Enabled { get; set; } = true;

    public int DrawCount { get; private set; }

    public void Record(in FrameContext frame) {

        var cmd = frame.CommandBuffer;

        cmd.BindPipeline(VkPipelineBindPoint.Graphics, _singleSided);
        cmd.BindDescriptorSet(VkPipelineBindPoint.Graphics, _layout, 0, frame.FrameSet, []);

        _models.ResetStats();

        foreach (var row in _world.Query<Transform, ModelInstance>()) {

            ref readonly var transform = ref row.C1;

            var rotation = float4x4.CreateFromQuaternion(SpaceMath.ToFloat(transform.Rotation));
            var entity = rotation * float4x4.CreateTranslation(_origin.ToLocal(transform.Position));

            _models.Draw(cmd, _layout, _singleSided, _doubleSided, row.C2.Model, entity, rotation);

        }

        DrawCount = _models.DrawCalls;

    }

    public void Dispose() {

        _device.DestroyPipeline(_singleSided, null);
        _device.DestroyPipeline(_doubleSided, null);
        _device.DestroyPipelineLayout(_layout, null);

    }

}
