using System.Runtime.InteropServices;
using Brutal;
using Brutal.Logging;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using FullThrust.Assets;
using FullThrust.Core.Rendering;
using FullThrust.Core.Space;

namespace FullThrust.Game.Rendering;

// Mirrors the Material block in Shaders/mesh.frag.
[StructLayout(LayoutKind.Sequential)]
public struct MaterialUniforms {

    public float4 BaseColor;

    // w is the normal map scale.
    public float4 Emissive;

    // metallic, roughness, occlusion strength, alpha cutoff.
    public float4 Params;

    // x alpha mask enabled.
    public float4 Flags;

}

// GPU copies of every model in a ModelCatalog, plus each definition's placement (orientation and fit).
public sealed class ModelLibrary : IDisposable {

    public const int MaterialTextureCount = 5;

    // Spec maximum for minUniformBufferOffsetAlignment, so offsets are valid on every device.
    private const int MaterialStride = 256;

    private static readonly LogCategory Log = LogCategory.Make("Models");

    private readonly DeviceEx _device;

    private readonly List<GpuModel> _models = [];
    private readonly List<GpuTexture> _textures = [];

    private readonly Placement[] _placements;

    private readonly VkSampler _sampler;
    private readonly VkDescriptorPool _pool;
    private readonly BufferEx _materials;

    private readonly GpuTexture _white;
    private readonly GpuTexture _whiteSrgb;
    private readonly GpuTexture _flatNormal;

    public ModelLibrary(VulkanContext context, GpuUploader uploader, ModelCatalog catalog, string modelsRoot) {

        _device = context.Device;

        MaterialLayout = Descriptors.CreateLayout(_device, [

            Descriptors.Binding(0, VkDescriptorType.UniformBuffer, VkShaderStageFlags.FragmentBit),
            Descriptors.Binding(1, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.FragmentBit),
            Descriptors.Binding(2, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.FragmentBit),
            Descriptors.Binding(3, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.FragmentBit),
            Descriptors.Binding(4, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.FragmentBit),
            Descriptors.Binding(5, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.FragmentBit),

        ]);

        _sampler = CreateSampler(context);

        _white = new GpuTexture(uploader, "White", [255, 255, 255, 255], 1, 1, srgb: false);
        _whiteSrgb = new GpuTexture(uploader, "WhiteSrgb", [255, 255, 255, 255], 1, 1, srgb: true);
        _flatNormal = new GpuTexture(uploader, "FlatNormal", [128, 128, 255, 255], 1, 1, srgb: false);

        var files = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var data = new List<ModelData>();

        _placements = new Placement[catalog.Count];

        for (var i = 0; i < catalog.Count; i++) {

            var definition = catalog[i];

            if (!files.TryGetValue(definition.File, out var index)) {

                var model = GltfModelLoader.Load(Path.Combine(modelsRoot, definition.File));

                Log.Info($"Loaded {definition.File}: {model.TriangleCount} triangles, {model.Materials.Length} materials, {model.Textures.Length} textures");

                index = data.Count;
                files[definition.File] = index;
                data.Add(model);

            }

            _placements[i] = Placement.Create(data[index], definition, index);

        }

        var materialCount = 0;

        foreach (var model in data) {

            materialCount += model.Materials.Length;

        }

        _pool = Descriptors.CreatePool(_device, Math.Max(1, materialCount), [

            new VkDescriptorPoolSize { Type = VkDescriptorType.UniformBuffer, DescriptorCount = Math.Max(1, materialCount) },
            new VkDescriptorPoolSize { Type = VkDescriptorType.CombinedImageSampler, DescriptorCount = Math.Max(1, materialCount * MaterialTextureCount) },

        ]);

        _materials = _device.CreateBuffer(new BufferEx.CreateInfo {

            Name = "Materials",
            BufferSize = new ByteSize((nuint)(Math.Max(1, materialCount) * MaterialStride)),
            BufferUsage = VkBufferUsageFlags.UniformBufferBit,
            AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,

        });

        var materialBase = 0;

        using (var mapped = _materials.Map()) {

            var bytes = mapped.AsSpan<byte>();

            foreach (var model in data) {

                _models.Add(Upload(uploader, model, bytes, ref materialBase));

            }

        }

        uploader.Flush();

    }

    public VkDescriptorSetLayout MaterialLayout { get; }

    public int ModelCount => _models.Count;

    public int DrawCalls { get; private set; }

    public void ResetStats() => DrawCalls = 0;

    private GpuModel Upload(GpuUploader uploader, ModelData model, Span<byte> materialBytes, ref int materialBase) {

        var textures = new GpuTexture[model.Textures.Length];

        for (var i = 0; i < textures.Length; i++) {

            var texture = model.Textures[i];

            textures[i] = new GpuTexture(uploader, texture.Name, texture.Pixels, texture.Width, texture.Height, texture.Srgb);
            _textures.Add(textures[i]);

        }

        var sets = new VkDescriptorSet[model.Materials.Length];
        var doubleSided = new bool[model.Materials.Length];

        for (var i = 0; i < model.Materials.Length; i++) {

            var material = model.Materials[i];
            var offset = (materialBase + i) * MaterialStride;

            MemoryMarshal.Write(materialBytes[offset..], new MaterialUniforms {

                BaseColor = material.BaseColor,
                Emissive = new float4(material.Emissive, material.NormalScale),
                Params = new float4(material.Metallic, material.Roughness, material.OcclusionStrength, material.AlphaCutoff),
                Flags = new float4(material.AlphaMode == AlphaMode.Mask ? 1 : 0, 0, 0, 0),

            });

            var set = Descriptors.Allocate(_device, _pool, MaterialLayout);

            WriteMaterialBuffer(set, offset);

            Descriptors.WriteImage(_device, set, 1, Pick(textures, material.BaseColorTexture, _whiteSrgb), _sampler);
            Descriptors.WriteImage(_device, set, 2, Pick(textures, material.MetallicRoughnessTexture, _white), _sampler);
            Descriptors.WriteImage(_device, set, 3, Pick(textures, material.NormalTexture, _flatNormal), _sampler);
            Descriptors.WriteImage(_device, set, 4, Pick(textures, material.OcclusionTexture, _white), _sampler);
            Descriptors.WriteImage(_device, set, 5, Pick(textures, material.EmissiveTexture, _whiteSrgb), _sampler);

            sets[i] = set;
            doubleSided[i] = material.DoubleSided;

        }

        materialBase += model.Materials.Length;

        var vertices = uploader.CreateDeviceBuffer(model.Name + ".Vertices", model.Vertices.AsSpan(), VkBufferUsageFlags.VertexBufferBit);
        var indices = uploader.CreateDeviceBuffer(model.Name + ".Indices", model.Indices.AsSpan(), VkBufferUsageFlags.IndexBufferBit);

        return new GpuModel(model, vertices, indices, sets, doubleSided);

    }

    private static VkImageView Pick(GpuTexture[] textures, int index, GpuTexture fallback) => index >= 0 ? textures[index].View : fallback.View;

    private unsafe void WriteMaterialBuffer(VkDescriptorSet set, int offset) {

        var info = new VkDescriptorBufferInfo { Buffer = _materials.VkBuffer, Offset = new ByteSize64((ulong)offset), Range = ByteSize.Of<MaterialUniforms>() };

        _device.UpdateDescriptorSets([

            new VkWriteDescriptorSet {

                DstSet = set,
                DstBinding = 0,
                DescriptorCount = 1,
                DescriptorType = VkDescriptorType.UniformBuffer,
                BufferInfo = &info,

            },

        ], []);

    }

    private static VkSampler CreateSampler(VulkanContext context) {

        var anisotropy = context.SamplerAnisotropy;

        return context.Device.CreateSampler(new VkSamplerCreateInfo {

            MagFilter = VkFilter.Linear,
            MinFilter = VkFilter.Linear,
            MipmapMode = VkSamplerMipmapMode.Linear,
            AddressModeU = VkSamplerAddressMode.Repeat,
            AddressModeV = VkSamplerAddressMode.Repeat,
            AddressModeW = VkSamplerAddressMode.Repeat,
            AnisotropyEnable = anisotropy > 1,
            MaxAnisotropy = anisotropy,
            MaxLod = 32,

        }, null);

    }

    // Records every instance of a catalog model; entity is the render-local placement of its owner.
    public void Draw(CommandBuffer cmd, VkPipelineLayout layout, VkPipeline single, VkPipeline both, int definition, in float4x4 entity, in float4x4 entityRotation) {

        ref readonly var placement = ref _placements[definition];

        var model = _models[placement.Model];
        var source = model.Data;

        model.Bind(cmd);

        var boundMaterial = -1;
        var boundBoth = -1;

        for (var i = 0; i < placement.Locals.Length; i++) {

            var world = placement.Locals[i] * entity;
            var normal = placement.Normals[i] * entityRotation;

            var push = new MeshPush {

                Model = world,
                Normal0 = new float4(normal.X.X, normal.X.Y, normal.X.Z, 0),
                Normal1 = new float4(normal.Y.X, normal.Y.Y, normal.Y.Z, 0),
                Normal2 = new float4(normal.Z.X, normal.Z.Y, normal.Z.Z, 0),

            };

            cmd.PushConstants(layout, VkShaderStageFlags.VertexBit, ByteSize.Zero, push);

            var mesh = source.Meshes[source.Instances[i].Mesh];

            for (var p = mesh.FirstPrimitive; p < mesh.FirstPrimitive + mesh.PrimitiveCount; p++) {

                var primitive = source.Primitives[p];
                var twoSided = model.DoubleSided[primitive.Material] ? 1 : 0;

                if (twoSided != boundBoth) {

                    cmd.BindPipeline(VkPipelineBindPoint.Graphics, twoSided == 1 ? both : single);
                    boundBoth = twoSided;

                }

                if (primitive.Material != boundMaterial) {

                    cmd.BindDescriptorSet(VkPipelineBindPoint.Graphics, layout, 1, model.Sets[primitive.Material], []);
                    boundMaterial = primitive.Material;

                }

                cmd.DrawIndexed(primitive.IndexCount, 1, primitive.FirstIndex, 0, 0);
                DrawCalls++;

            }

        }

    }

    public void Dispose() {

        foreach (var model in _models) {

            model.Dispose();

        }

        foreach (var texture in _textures) {

            texture.Dispose();

        }

        _white.Dispose();
        _whiteSrgb.Dispose();
        _flatNormal.Dispose();
        _materials.Dispose();

        _device.DestroyDescriptorPool(_pool, null);
        _device.DestroyDescriptorSetLayout(MaterialLayout, null);
        _device.DestroySampler(_sampler, null);

    }

    private sealed class GpuModel : IDisposable {

        private readonly BufferEx _vertices;
        private readonly BufferEx _indices;

        public GpuModel(ModelData data, BufferEx vertices, BufferEx indices, VkDescriptorSet[] sets, bool[] doubleSided) {

            Data = data;
            Sets = sets;
            DoubleSided = doubleSided;

            _vertices = vertices;
            _indices = indices;

        }

        public ModelData Data { get; }

        public VkDescriptorSet[] Sets { get; }

        public bool[] DoubleSided { get; }

        public void Bind(CommandBuffer cmd) {

            cmd.BindVertexBuffer(0, _vertices.VkBuffer);
            cmd.BindIndexBuffer(_indices.VkBuffer, ByteSize.Zero, VkIndexType.UInt32);

        }

        public void Dispose() {

            _vertices.Dispose();
            _indices.Dispose();

        }

    }

    // Per-instance model-space matrices with the definition's orientation and fit baked in.
    private readonly struct Placement {

        public readonly int Model;

        public readonly float4x4[] Locals;
        public readonly float4x4[] Normals;

        private Placement(int model, float4x4[] locals, float4x4[] normals) {

            Model = model;
            Locals = locals;
            Normals = normals;

        }

        public static Placement Create(ModelData model, ModelDefinition definition, int index) {

            var radians = definition.RotationDeg * (Math.PI / 180);
            var rotation = float4x4.CreateRotationX((float)radians.X) * float4x4.CreateRotationY((float)radians.Y) * float4x4.CreateRotationZ((float)radians.Z);

            var min = new float3(float.MaxValue);
            var max = new float3(float.MinValue);

            foreach (var instance in model.Instances) {

                var transform = instance.Transform * rotation;
                var mesh = model.Meshes[instance.Mesh];

                for (var p = mesh.FirstPrimitive; p < mesh.FirstPrimitive + mesh.PrimitiveCount; p++) {

                    var primitive = model.Primitives[p];

                    for (var i = primitive.FirstIndex; i < primitive.FirstIndex + primitive.IndexCount; i++) {

                        var position = float3.Transform(model.Vertices[model.Indices[i]].Position, transform);

                        min = float3.Min(min, position);
                        max = float3.Max(max, position);

                    }

                }

            }

            var fit = rotation * FitMatrix(definition, min, max) * float4x4.CreateTranslation(SpaceMath.ToFloat(definition.Offset));
            var locals = new float4x4[model.Instances.Length];
            var normals = new float4x4[model.Instances.Length];

            for (var i = 0; i < locals.Length; i++) {

                locals[i] = model.Instances[i].Transform * fit;

                float4x4.Invert(locals[i], out var inverse);

                normals[i] = float4x4.Transpose(inverse);

            }

            return new Placement(index, locals, normals);

        }

        private static float4x4 FitMatrix(ModelDefinition definition, float3 min, float3 max) {

            if (definition.Fit == ModelFit.None) {

                return float4x4.Identity;

            }

            var size = float3.Max(max - min, new float3(1e-6f));
            var center = (min + max) * 0.5f;
            var target = SpaceMath.ToFloat(definition.FitSize);
            var ratio = target / size;

            var scale = definition.Fit switch {

                ModelFit.Stretch => ratio,
                ModelFit.Length => new float3(ratio.Y),
                _ => new float3(MathF.Min(ratio.X, MathF.Min(ratio.Y, ratio.Z))),

            };

            return float4x4.CreateTranslation(-center) * float4x4.CreateScale(scale);

        }

    }

}

[StructLayout(LayoutKind.Sequential)]
public struct MeshPush {

    public float4x4 Model;

    public float4 Normal0;
    public float4 Normal1;
    public float4 Normal2;

}
