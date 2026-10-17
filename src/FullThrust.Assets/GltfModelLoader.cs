using System.Buffers.Binary;
using Brutal.GltfApi;
using Brutal.Numerics;
using Brutal.TextureApi;

namespace FullThrust.Assets;

// Flattens a glTF 2.0 file (via Brutal.Gltf) into one vertex/index buffer, materials and decoded textures.
public static class GltfModelLoader {

    private static readonly HashSet<string> SupportedRequiredExtensions = new(StringComparer.Ordinal) {

        "KHR_materials_emissive_strength",
        "KHR_texture_transform",

    };

    public static ModelData Load(string path) {

        using var loader = new GltfLoader(path);

        var gltf = loader.GltfJson;

        foreach (var extension in gltf.ExtensionsRequired ?? []) {

            if (!SupportedRequiredExtensions.Contains(extension)) {

                throw new NotSupportedException($"{Path.GetFileName(path)} requires glTF extension {extension}, which is not supported.");

            }

        }

        var builder = new Builder(loader, Path.GetFileNameWithoutExtension(path));

        builder.ReadMaterials();
        builder.ReadMeshes();
        builder.ReadNodes();

        return builder.Finish();

    }

    private sealed class Builder {

        private readonly GltfLoader _loader;
        private readonly Gltf _gltf;
        private readonly ModelData _model;

        private readonly List<MeshVertex> _vertices = [];
        private readonly List<uint> _indices = [];
        private readonly List<PrimitiveData> _primitives = [];
        private readonly List<MeshRange> _meshes = [];
        private readonly List<MeshInstance> _instances = [];
        private readonly List<MaterialData> _materials = [];
        private readonly List<TextureData> _textures = [];

        private readonly Dictionary<(int Image, bool Srgb), int> _textureLookup = [];

        private int _defaultMaterial = -1;

        public Builder(GltfLoader loader, string name) {

            _loader = loader;
            _gltf = loader.GltfJson;
            _model = new ModelData { Name = name };

        }

        public void ReadMaterials() {

            foreach (var source in _gltf.Materials ?? []) {

                var pbr = source.PbrMetallicRoughness;

                var material = new MaterialData {

                    Name = source.Name ?? "",
                    BaseColor = pbr?.BaseColorFactor ?? new float4(1),
                    Metallic = pbr?.MetallicFactor ?? 1,
                    Roughness = pbr?.RoughnessFactor ?? 1,
                    Emissive = source.EmissiveFactor,
                    AlphaMode = (AlphaMode)(int)source.AlphaMode,
                    AlphaCutoff = source.AlphaCutoff,
                    DoubleSided = source.DoubleSided,

                };

                material.BaseColorTexture = Texture(pbr?.BaseColorTexture?.Index, srgb: true);
                material.MetallicRoughnessTexture = Texture(pbr?.MetallicRoughnessTexture?.Index, srgb: false);
                material.EmissiveTexture = Texture(source.EmissiveTexture?.Index, srgb: true);

                if (source.NormalTexture is { } normal) {

                    material.NormalTexture = Texture(normal.Index, srgb: false);
                    material.NormalScale = normal.Scale;

                }

                if (source.OcclusionTexture is { } occlusion) {

                    material.OcclusionTexture = Texture(occlusion.Index, srgb: false);
                    material.OcclusionStrength = occlusion.Strength;

                }

                _materials.Add(material);

            }

        }

        // Images only reachable through EXT_texture_webp have no source we can decode, so they are dropped.
        private int Texture(int? textureIndex, bool srgb) {

            if (textureIndex is not { } index || _gltf.Textures is null || index >= _gltf.Textures.Length) {

                return -1;

            }

            if (_gltf.Textures[index].Source is not { } image) {

                return -1;

            }

            if (_textureLookup.TryGetValue((image, srgb), out var existing)) {

                return existing;

            }

            var texture = _loader.GetOrLoadImage(image);
            var (width, height) = (texture.Extent.X, texture.Extent.Y);

            var data = new TextureData {

                Name = _gltf.Images[image].Name ?? $"image{image}",
                Width = width,
                Height = height,
                Pixels = ToRgba8(texture, width, height),
                Srgb = srgb,
                Png = EncodedPng(image),

            };

            _textures.Add(data);
            _textureLookup[(image, srgb)] = _textures.Count - 1;

            return _textures.Count - 1;

        }

        private byte[]? EncodedPng(int image) {

            var source = _gltf.Images[image];

            if (source.BufferView is { } view && source.MimeType == Image.MimeTypePng) {

                return _loader.GetBufferView(view).ToArray();

            }

            return null;

        }

        private static byte[] ToRgba8(ITexture texture, int width, int height) {

            var source = texture.ImageData(0, 0, 0);
            var pitch = (int)texture.RowPitch(0).Bytes;
            var pixels = new byte[width * height * 4];

            var channels = texture.Format switch {

                TextureFormat.R8G8B8A8_UNorm or TextureFormat.R8G8B8A8_SRGB => 4,
                TextureFormat.R8G8B8_UNorm or TextureFormat.R8G8B8_SRGB => 3,
                TextureFormat.R8G8_UNorm => 2,
                TextureFormat.R8_UNorm => 1,
                _ => throw new NotSupportedException($"Texture format {texture.Format} is not supported."),

            };

            for (var y = 0; y < height; y++) {

                var row = source.Slice(y * pitch, width * channels);

                for (var x = 0; x < width; x++) {

                    var o = (y * width + x) * 4;
                    var s = x * channels;

                    switch (channels) {

                        case 4:
                            pixels[o] = row[s]; pixels[o + 1] = row[s + 1]; pixels[o + 2] = row[s + 2]; pixels[o + 3] = row[s + 3];
                            break;

                        case 3:
                            pixels[o] = row[s]; pixels[o + 1] = row[s + 1]; pixels[o + 2] = row[s + 2]; pixels[o + 3] = 255;
                            break;

                        case 2:
                            pixels[o] = pixels[o + 1] = pixels[o + 2] = row[s]; pixels[o + 3] = row[s + 1];
                            break;

                        default:
                            pixels[o] = pixels[o + 1] = pixels[o + 2] = row[s]; pixels[o + 3] = 255;
                            break;

                    }

                }

            }

            return pixels;

        }

        public void ReadMeshes() {

            foreach (var mesh in _gltf.Meshes ?? []) {

                var first = _primitives.Count;

                foreach (var primitive in mesh.Primitives) {

                    ReadPrimitive(primitive);

                }

                _meshes.Add(new MeshRange(first, _primitives.Count - first, mesh.Name ?? ""));

            }

        }

        private void ReadPrimitive(MeshPrimitive primitive) {

            if (primitive.Mode != MeshPrimitive.ModeEnum.Triangles) {

                return;

            }

            if (primitive.Extensions?.ContainsKey("KHR_draco_mesh_compression") == true) {

                throw new NotSupportedException("Draco-compressed primitives are not supported; decompress the model first.");

            }

            if (!primitive.Attributes.TryGetValue("POSITION", out var positionAccessor)) {

                return;

            }

            var baseVertex = _vertices.Count;
            var positions = ReadFloats(positionAccessor, 3);
            var count = positions.Length / 3;

            var normals = primitive.Attributes.TryGetValue("NORMAL", out var n) ? ReadFloats(n, 3) : null;
            var uvs = primitive.Attributes.TryGetValue("TEXCOORD_0", out var t) ? ReadFloats(t, 2) : null;
            var tangents = primitive.Attributes.TryGetValue("TANGENT", out var g) ? ReadFloats(g, 4) : null;

            for (var i = 0; i < count; i++) {

                var vertex = new MeshVertex(
                    new float3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]),
                    normals is null ? float3.Zero : new float3(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2]),
                    uvs is null ? float2.Zero : new float2(uvs[i * 2], uvs[i * 2 + 1]));

                if (tangents is not null) {

                    vertex.Tangent = new float4(tangents[i * 4], tangents[i * 4 + 1], tangents[i * 4 + 2], tangents[i * 4 + 3]);

                }

                _vertices.Add(vertex);

            }

            var firstIndex = _indices.Count;

            if (primitive.Indices is { } indexAccessor) {

                foreach (var index in ReadIndices(indexAccessor)) {

                    _indices.Add((uint)(baseVertex + index));

                }

            } else {

                for (var i = 0; i < count; i++) {

                    _indices.Add((uint)(baseVertex + i));

                }

            }

            var indexCount = _indices.Count - firstIndex;
            var vertices = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices);
            var indices = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_indices).Slice(firstIndex, indexCount);

            if (normals is null) {

                MeshMath.ComputeNormals(vertices, indices);

            }

            var material = primitive.Material ?? DefaultMaterial();

            if (tangents is null && _materials[material].NormalTexture >= 0) {

                MeshMath.ComputeTangents(vertices, indices);

            }

            _primitives.Add(new PrimitiveData(firstIndex, indexCount, material));

        }

        private int DefaultMaterial() {

            if (_defaultMaterial < 0) {

                _materials.Add(new MaterialData { Name = "Default", Metallic = 0, Roughness = 0.6f });
                _defaultMaterial = _materials.Count - 1;

            }

            return _defaultMaterial;

        }

        public void ReadNodes() {

            if (_gltf.Nodes is null) {

                return;

            }

            int[] roots;

            if (_gltf.Scenes is { Length: > 0 } scenes) {

                roots = scenes[_gltf.Scene ?? 0].Nodes ?? [];

            } else {

                var children = new HashSet<int>();

                foreach (var node in _gltf.Nodes) {

                    foreach (var child in node.Children ?? []) {

                        children.Add(child);

                    }

                }

                roots = Enumerable.Range(0, _gltf.Nodes.Length).Where(i => !children.Contains(i)).ToArray();

            }

            foreach (var root in roots) {

                Visit(root, float4x4.Identity);

            }

        }

        private void Visit(int index, float4x4 parent) {

            var node = _gltf.Nodes[index];
            var local = node.Matrix != float4x4.Identity
                ? node.Matrix
                : float4x4.CreateScale(node.Scale) * float4x4.CreateFromQuaternion(node.Rotation) * float4x4.CreateTranslation(node.Translation);

            var world = local * parent;

            if (node.Mesh is { } mesh) {

                _instances.Add(new MeshInstance(mesh, world));

            }

            foreach (var child in node.Children ?? []) {

                Visit(child, world);

            }

        }

        public ModelData Finish() {

            _model.Vertices = _vertices.ToArray();
            _model.Indices = _indices.ToArray();
            _model.Primitives = _primitives.ToArray();
            _model.Meshes = _meshes.ToArray();
            _model.Instances = _instances.ToArray();
            _model.Materials = _materials.ToArray();
            _model.Textures = _textures.ToArray();
            _model.ComputeBounds();

            return _model;

        }

        private float[] ReadFloats(int accessorIndex, int components) {

            var accessor = _gltf.Accessors[accessorIndex];

            if (accessor.Sparse is not null || accessor.BufferView is not { } viewIndex) {

                throw new NotSupportedException("Sparse accessors are not supported.");

            }

            var view = _gltf.BufferViews[viewIndex];
            var bytes = _loader.GetBufferView(viewIndex);
            var componentSize = ComponentSize(accessor.ComponentType);
            var stride = view.ByteStride ?? componentSize * components;
            var result = new float[accessor.Count * components];

            for (var i = 0; i < accessor.Count; i++) {

                var element = bytes.Slice(accessor.ByteOffset + i * stride);

                for (var c = 0; c < components; c++) {

                    result[i * components + c] = ReadComponent(element.Slice(c * componentSize), accessor.ComponentType, accessor.Normalized);

                }

            }

            return result;

        }

        private int[] ReadIndices(int accessorIndex) {

            var accessor = _gltf.Accessors[accessorIndex];

            if (accessor.BufferView is not { } viewIndex) {

                throw new NotSupportedException("Index accessors need a buffer view.");

            }

            var view = _gltf.BufferViews[viewIndex];
            var bytes = _loader.GetBufferView(viewIndex);
            var size = ComponentSize(accessor.ComponentType);
            var stride = view.ByteStride ?? size;
            var result = new int[accessor.Count];

            for (var i = 0; i < accessor.Count; i++) {

                var element = bytes.Slice(accessor.ByteOffset + i * stride);

                result[i] = accessor.ComponentType switch {

                    Accessor.ComponentTypeEnum.UnsignedByte => element[0],
                    Accessor.ComponentTypeEnum.UnsignedShort => BinaryPrimitives.ReadUInt16LittleEndian(element),
                    Accessor.ComponentTypeEnum.UnsignedInt => (int)BinaryPrimitives.ReadUInt32LittleEndian(element),
                    _ => throw new InvalidDataException($"Invalid index component type {accessor.ComponentType}."),

                };

            }

            return result;

        }

        private static int ComponentSize(Accessor.ComponentTypeEnum type) => type switch {

            Accessor.ComponentTypeEnum.Byte or Accessor.ComponentTypeEnum.UnsignedByte => 1,
            Accessor.ComponentTypeEnum.Short or Accessor.ComponentTypeEnum.UnsignedShort => 2,
            _ => 4,

        };

        private static float ReadComponent(ReadOnlySpan<byte> bytes, Accessor.ComponentTypeEnum type, bool normalized) {

            return type switch {

                Accessor.ComponentTypeEnum.Float => BinaryPrimitives.ReadSingleLittleEndian(bytes),
                Accessor.ComponentTypeEnum.UnsignedByte => normalized ? bytes[0] / 255f : bytes[0],
                Accessor.ComponentTypeEnum.Byte => normalized ? Math.Max((sbyte)bytes[0] / 127f, -1f) : (sbyte)bytes[0],
                Accessor.ComponentTypeEnum.UnsignedShort => normalized ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) / 65535f : BinaryPrimitives.ReadUInt16LittleEndian(bytes),
                Accessor.ComponentTypeEnum.Short => normalized ? Math.Max(BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32767f, -1f) : BinaryPrimitives.ReadInt16LittleEndian(bytes),
                Accessor.ComponentTypeEnum.UnsignedInt => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                _ => throw new InvalidDataException($"Unknown component type {type}."),

            };

        }

    }

}
