using System.Runtime.InteropServices;
using Brutal.GltfApi;
using Brutal.Numerics;

namespace FullThrust.Assets;

// Writes ModelData as a self-contained .glb through the Brutal.Gltf schema; textures are embedded as PNG.
public static class GltfModelWriter {

    public static void WriteGlb(ModelData model, string path, string generator = "FullThrust") {

        var buffer = new BinaryBufferBuilder();
        var gltf = new Gltf { Asset = new Asset { Version = "2.0", Generator = generator } };

        var accessors = new List<Accessor>();
        var views = new List<BufferView>();

        int AddView(ReadOnlySpan<byte> bytes, BufferView.TargetEnum? target) {

            var offset = buffer.Append(bytes);

            views.Add(new BufferView { Buffer = 0, ByteOffset = offset, ByteLength = bytes.Length, Target = target });

            return views.Count - 1;

        }

        int AddAccessor(int view, Accessor.TypeEnum type, int count, float[]? min = null, float[]? max = null, int byteOffset = 0, Accessor.ComponentTypeEnum component = Accessor.ComponentTypeEnum.Float) {

            accessors.Add(new Accessor { BufferView = view, ByteOffset = byteOffset, ComponentType = component, Count = count, Type = type, Min = min, Max = max });

            return accessors.Count - 1;

        }

        var vertexCount = model.Vertices.Length;
        var positions = new float3[vertexCount];
        var normals = new float3[vertexCount];
        var uvs = new float2[vertexCount];
        var tangents = new float4[vertexCount];
        var min = new float3(float.MaxValue);
        var max = new float3(float.MinValue);

        for (var i = 0; i < vertexCount; i++) {

            var v = model.Vertices[i];

            positions[i] = v.Position;
            normals[i] = v.Normal;
            uvs[i] = v.Uv;
            tangents[i] = v.Tangent;
            min = float3.Min(min, v.Position);
            max = float3.Max(max, v.Position);

        }

        var target = BufferView.TargetEnum.ArrayBuffer;

        var position = AddAccessor(AddView(MemoryMarshal.AsBytes<float3>(positions), target), Accessor.TypeEnum.Vec3, vertexCount, [min.X, min.Y, min.Z], [max.X, max.Y, max.Z]);
        var normal = AddAccessor(AddView(MemoryMarshal.AsBytes<float3>(normals), target), Accessor.TypeEnum.Vec3, vertexCount);
        var uv = AddAccessor(AddView(MemoryMarshal.AsBytes<float2>(uvs), target), Accessor.TypeEnum.Vec2, vertexCount);
        var tangent = AddAccessor(AddView(MemoryMarshal.AsBytes<float4>(tangents), target), Accessor.TypeEnum.Vec4, vertexCount);

        var indexView = AddView(MemoryMarshal.AsBytes<uint>(model.Indices), BufferView.TargetEnum.ElementArrayBuffer);

        gltf.Meshes = new Mesh[model.Meshes.Length];

        for (var m = 0; m < model.Meshes.Length; m++) {

            var range = model.Meshes[m];
            var primitives = new MeshPrimitive[range.PrimitiveCount];

            for (var p = 0; p < range.PrimitiveCount; p++) {

                var source = model.Primitives[range.FirstPrimitive + p];
                var indices = AddAccessor(indexView, Accessor.TypeEnum.Scalar, source.IndexCount, byteOffset: source.FirstIndex * sizeof(uint), component: Accessor.ComponentTypeEnum.UnsignedInt);

                primitives[p] = new MeshPrimitive {

                    Attributes = new Dictionary<string, int> { ["POSITION"] = position, ["NORMAL"] = normal, ["TEXCOORD_0"] = uv, ["TANGENT"] = tangent },
                    Indices = indices,
                    Material = source.Material,
                    Mode = MeshPrimitive.ModeEnum.Triangles,

                };

            }

            gltf.Meshes[m] = new Mesh { Name = range.Name, Primitives = primitives };

        }

        gltf.Nodes = new Node[model.Instances.Length];

        for (var i = 0; i < model.Instances.Length; i++) {

            gltf.Nodes[i] = new Node { Mesh = model.Instances[i].Mesh, Matrix = model.Instances[i].Transform };

        }

        gltf.Scenes = [new Scene { Nodes = Enumerable.Range(0, model.Instances.Length).ToArray() }];
        gltf.Scene = 0;

        WriteTextures(model, gltf, AddView);
        WriteMaterials(model, gltf);

        gltf.Accessors = accessors.ToArray();
        gltf.BufferViews = views.ToArray();

        var bytes = buffer.ToArray();

        gltf.Buffers = [new Brutal.GltfApi.Buffer { ByteLength = bytes.Length }];

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory)) {

            Directory.CreateDirectory(directory);

        }

        gltf.SaveBinaryModel(bytes, path);

    }

    private delegate int AddViewFn(ReadOnlySpan<byte> bytes, BufferView.TargetEnum? target);

    private static void WriteTextures(ModelData model, Gltf gltf, AddViewFn addView) {

        if (model.Textures.Length == 0) {

            return;

        }

        gltf.Samplers = [new Sampler { MagFilter = Sampler.MagFilterEnum.Linear, MinFilter = Sampler.MinFilterEnum.LinearMipmapLinear, WrapS = Sampler.WrapSEnum.Repeat, WrapT = Sampler.WrapTEnum.Repeat }];
        gltf.Images = new Image[model.Textures.Length];
        gltf.Textures = new Texture[model.Textures.Length];

        for (var i = 0; i < model.Textures.Length; i++) {

            var texture = model.Textures[i];
            var png = texture.Png ?? PngEncoder.Encode(texture.Pixels, texture.Width, texture.Height);

            gltf.Images[i] = new Image { Name = texture.Name, MimeType = Image.MimeTypePng, BufferView = addView(png, null) };
            gltf.Textures[i] = new Texture { Sampler = 0, Source = i };

        }

    }

    private static void WriteMaterials(ModelData model, Gltf gltf) {

        gltf.Materials = new Material[model.Materials.Length];

        for (var i = 0; i < model.Materials.Length; i++) {

            var source = model.Materials[i];

            var material = new Material {

                Name = source.Name,
                EmissiveFactor = source.Emissive,
                DoubleSided = source.DoubleSided,

                PbrMetallicRoughness = new MaterialPbrMetallicRoughness {

                    BaseColorFactor = source.BaseColor,
                    MetallicFactor = source.Metallic,
                    RoughnessFactor = source.Roughness,
                    BaseColorTexture = Info(source.BaseColorTexture),
                    MetallicRoughnessTexture = Info(source.MetallicRoughnessTexture),

                },

                EmissiveTexture = Info(source.EmissiveTexture),

            };

            if (source.NormalTexture >= 0) {

                material.NormalTexture = new MaterialNormalTextureInfo { Index = source.NormalTexture, Scale = source.NormalScale };

            }

            if (source.OcclusionTexture >= 0) {

                material.OcclusionTexture = new MaterialOcclusionTextureInfo { Index = source.OcclusionTexture, Strength = source.OcclusionStrength };

            }

            gltf.Materials[i] = material;

        }

    }

    private static TextureInfo? Info(int texture) => texture >= 0 ? new TextureInfo { Index = texture } : null;

    // GLB requires each buffer view to start 4-byte aligned.
    private sealed class BinaryBufferBuilder {

        private readonly MemoryStream _stream = new();

        public int Append(ReadOnlySpan<byte> bytes) {

            while (_stream.Length % 4 != 0) {

                _stream.WriteByte(0);

            }

            var offset = (int)_stream.Length;

            _stream.Write(bytes);

            return offset;

        }

        public byte[] ToArray() {

            while (_stream.Length % 4 != 0) {

                _stream.WriteByte(0);

            }

            return _stream.ToArray();

        }

    }

}
