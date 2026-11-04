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
public sealed class MeshPass : IRenderPass, IDepthCaster {

    private readonly DeviceEx _device;

    private readonly World _world;
    private readonly FloatingOrigin _origin;
    private readonly ModelLibrary _models;
    private readonly Camera _camera;

    private readonly VkPipelineLayout _layout;
    private readonly VkPipeline _singleSided;
    private readonly VkPipeline _doubleSided;

    private readonly VkPipeline _depthSingleSided;
    private readonly VkPipeline _depthDoubleSided;
    private readonly VkPipeline _shadow;
    private readonly VkPipeline _cutoutDepth;
    private readonly VkPipeline _cutoutShadow;

    public unsafe MeshPass(DeviceEx device, VkRenderPass renderPass, VkRenderPass depthPass, VkDescriptorSetLayout frameLayout, ShaderLibrary shaders, World world, FloatingOrigin origin, ModelLibrary models, Camera camera) {

        _device = device;
        _world = world;
        _origin = origin;
        _models = models;
        _camera = camera;

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

        desc.RenderPass = depthPass;
        desc.DepthOnly = true;
        desc.FragmentShader = null;
        desc.Cull = VkCullModeFlags.BackBit;

        _depthSingleSided = Pipelines.Create(device, shaders, desc);

        desc.Cull = VkCullModeFlags.None;

        _depthDoubleSided = Pipelines.Create(device, shaders, desc);

        desc.FragmentShader = "mesh_cutout.frag";
        _cutoutDepth = Pipelines.Create(device, shaders, desc);
        desc.VertexShader = "mesh_shadow.vert";
        desc.ShadowCaster = true;

        _cutoutShadow = Pipelines.Create(device, shaders, desc);
        desc.FragmentShader = null;
        _shadow = Pipelines.Create(device, shaders, desc);

    }

    public string Name => "Meshes";

    public RenderStage Stage => RenderStage.Opaque;

    public bool Enabled { get; set; } = true;

    public bool CastsDepth => Enabled;

    public int DrawCount { get; private set; }

    public void Record(in FrameContext frame) {

        var cmd = frame.CommandBuffer;

        cmd.BindPipeline(VkPipelineBindPoint.Graphics, _singleSided);
        cmd.BindDescriptorSet(VkPipelineBindPoint.Graphics, _layout, 0, frame.FrameSet, []);

        _models.ResetStats();

        DrawAll(cmd, _singleSided, _doubleSided, default, Frustum.FromMatrix(frame.View * frame.Projection), false, -1);

        DrawCount = _models.DrawCalls;

    }

    public void RecordDepth(in DepthContext context) {

        var cmd = context.CommandBuffer;
        var single = context.View == DepthView.Shadow ? _shadow : _depthSingleSided;
        var both = context.View == DepthView.Shadow ? _shadow : _depthDoubleSided;

        cmd.BindPipeline(VkPipelineBindPoint.Graphics, single);
        cmd.BindDescriptorSet(VkPipelineBindPoint.Graphics, _layout, 0, context.FrameSet, []);

        var cutout = context.View == DepthView.Shadow ? _cutoutShadow : _cutoutDepth;
        DrawAll(cmd, single, both, cutout, context.Frustum, true, context.Cascade);

    }

    private void DrawAll(CommandBuffer cmd, VkPipeline single, VkPipeline both, VkPipeline cutout, in Frustum frustum, bool depthOnly, int cascade) {

        foreach (var row in _world.Query<Transform, ModelInstance>()) {

            ref readonly var transform = ref row.C1;
            var range = row.C2.DetailRange;
            if (range > 0 && (transform.Position - _camera.Position).LengthSquared() > range * range) {

                continue;

            }

            var rotation = float4x4.CreateFromQuaternion(SpaceMath.ToFloat(transform.Rotation));
            var entity = rotation * float4x4.CreateTranslation(_origin.ToLocal(transform.Position));

            _models.Draw(cmd, _layout, single, both, cutout, row.C2.Model, entity, rotation, frustum, depthOnly, cascade);

            if (row.C2.Shroud is { } shroud) {

                _models.Draw(cmd, _layout, single, both, cutout, shroud, entity, rotation, frustum, depthOnly, cascade);

            }

        }

    }

    public void Dispose() {

        _device.DestroyPipeline(_singleSided, null);
        _device.DestroyPipeline(_doubleSided, null);
        _device.DestroyPipeline(_depthSingleSided, null);
        _device.DestroyPipeline(_depthDoubleSided, null);
        _device.DestroyPipeline(_shadow, null);
        _device.DestroyPipeline(_cutoutDepth, null);
        _device.DestroyPipeline(_cutoutShadow, null);
        _device.DestroyPipelineLayout(_layout, null);

    }

}
