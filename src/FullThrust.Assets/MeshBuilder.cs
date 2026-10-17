using Brutal.Numerics;

namespace FullThrust.Assets;

public struct LatheOptions {

    public int Segments;

    // Texture repeats around the circumference, and metres of profile length per V unit.
    public float URepeat;
    public float VMetres;

    // Adjacent profile segments meeting at less than this angle share smoothed normals.
    public float SmoothAngleDeg;

    public float StartAngle;
    public float EndAngle;

    public static LatheOptions Default => new() { Segments = 64, URepeat = 1, VMetres = 1, SmoothAngleDeg = 30, StartAngle = 0, EndAngle = MathF.Tau };

}

// Builds procedural meshes and composes existing models into one ModelData. Front faces wind counter-clockwise.
public sealed class MeshBuilder {

    private readonly List<MeshVertex> _vertices = [];
    private readonly Dictionary<int, List<uint>> _indices = [];

    private readonly List<MaterialData> _materials = [];
    private readonly List<TextureData> _textures = [];

    private readonly List<(ModelData Source, int MaterialBase, int TextureBase, float4x4[] Transforms)> _sources = [];

    public int AddMaterial(MaterialData material) {

        _materials.Add(material);

        return _materials.Count - 1;

    }

    public int AddTexture(TextureData texture) {

        _textures.Add(texture);

        return _textures.Count - 1;

    }

    // Revolves an (r, y) profile about +Y. Profiles run bottom to top so normals face outwards.
    public void Lathe(ReadOnlySpan<float2> profile, int material, LatheOptions options, float4x4? transform = null) {

        var matrix = transform ?? float4x4.Identity;
        var segments = Math.Max(3, options.Segments);
        var count = profile.Length;

        Span<float2> segmentNormals = stackalloc float2[Math.Max(1, count - 1)];
        Span<float> arc = stackalloc float[count];

        for (var i = 0; i + 1 < count; i++) {

            var d = profile[i + 1] - profile[i];

            segmentNormals[i] = float2.NormalizeOrZero(new float2(d.Y, -d.X));
            arc[i + 1] = arc[i] + d.Length();

        }

        var cosLimit = MathF.Cos(options.SmoothAngleDeg * MathF.PI / 180);
        var indices = Indices(material);

        for (var i = 0; i + 1 < count; i++) {

            var d = profile[i + 1] - profile[i];

            if (d.LengthSquared() < 1e-12f) {

                continue;

            }

            var n0 = SmoothedNormal(segmentNormals, i, i - 1, cosLimit);
            var n1 = SmoothedNormal(segmentNormals, i, i + 1, cosLimit);

            var baseVertex = (uint)_vertices.Count;

            for (var s = 0; s <= segments; s++) {

                var t = (float)s / segments;
                var angle = options.StartAngle + (options.EndAngle - options.StartAngle) * t;
                var (sin, cos) = MathF.SinCos(angle);
                var u = t * options.URepeat;

                AddRevolved(profile[i], n0, cos, sin, new float2(u, -arc[i] / options.VMetres), matrix);
                AddRevolved(profile[i + 1], n1, cos, sin, new float2(u, -arc[i + 1] / options.VMetres), matrix);

            }

            for (var s = 0; s < segments; s++) {

                var a = baseVertex + (uint)(s * 2);
                var b = a + 2;
                var c = a + 1;
                var e = a + 3;

                indices.Add(a); indices.Add(b); indices.Add(e);
                indices.Add(a); indices.Add(e); indices.Add(c);

            }

        }

    }

    private static float2 SmoothedNormal(ReadOnlySpan<float2> normals, int segment, int neighbour, float cosLimit) {

        var own = normals[segment];

        if (neighbour < 0 || neighbour >= normals.Length || normals[neighbour] == float2.Zero) {

            return own;

        }

        return float2.Dot(own, normals[neighbour]) >= cosLimit ? float2.NormalizeOrZero(own + normals[neighbour]) : own;

    }

    private void AddRevolved(float2 point, float2 normal, float cos, float sin, float2 uv, in float4x4 matrix) {

        var position = new float3(point.X * cos, point.Y, -point.X * sin);
        var n = new float3(normal.X * cos, normal.Y, -normal.X * sin);

        AddVertex(position, n, uv, matrix);

    }

    private void AddVertex(float3 position, float3 normal, float2 uv, in float4x4 matrix) {

        float4x4.Invert(matrix, out var inverse);

        var normalMatrix = float4x4.Transpose(inverse);

        _vertices.Add(new MeshVertex(float3.Transform(position, matrix), float3.NormalizeOrZero(float3.TransformNormal(normal, normalMatrix)), uv));

    }

    // Axis-aligned box in local space; UVs are planar per face, scaled to metres.
    public void Box(float3 min, float3 max, int material, float4x4? transform = null, float uvMetres = 1) {

        var matrix = transform ?? float4x4.Identity;
        var indices = Indices(material);

        Span<float3> normals = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];

        var center = (min + max) * 0.5f;
        var half = (max - min) * 0.5f;

        foreach (var n in normals) {

            var u = MathF.Abs(n.Y) > 0.5f ? new float3(1, 0, 0) : float3.Cross(float3.UnitY, n);
            var v = float3.Cross(n, u);

            var baseVertex = (uint)_vertices.Count;

            for (var corner = 0; corner < 4; corner++) {

                var su = corner is 1 or 2 ? 1f : -1f;
                var sv = corner >= 2 ? 1f : -1f;
                var local = n + u * su + v * sv;
                var position = center + local * half;
                var uv = new float2(float3.Dot(position, u), -float3.Dot(position, v)) / uvMetres;

                AddVertex(position, n, uv, matrix);

            }

            indices.Add(baseVertex); indices.Add(baseVertex + 1); indices.Add(baseVertex + 2);
            indices.Add(baseVertex); indices.Add(baseVertex + 2); indices.Add(baseVertex + 3);

        }

    }

    // Capped cylinder along +Y between y0 and y1.
    public void Cylinder(float radius, float y0, float y1, int material, int segments = 24, float4x4? transform = null, bool caps = true) {

        var options = LatheOptions.Default;

        options.Segments = segments;
        options.SmoothAngleDeg = 10;

        if (caps) {

            Lathe([new(0, y0), new(radius, y0), new(radius, y1), new(0, y1)], material, options, transform);

        } else {

            Lathe([new(radius, y0), new(radius, y1)], material, options, transform);

        }

    }

    // A straight strut between two points with a square cross-section.
    public void Strut(float3 from, float3 to, float width, int material) {

        var axis = to - from;
        var length = axis.Length();

        if (length < 1e-5f) {

            return;

        }

        var up = axis / length;
        var rotation = floatQuat.Normalize(FromTo(float3.UnitY, up));
        var matrix = float4x4.CreateFromQuaternion(rotation) * float4x4.CreateTranslation(from);
        var h = width * 0.5f;

        Box(new float3(-h, 0, -h), new float3(h, length, h), material, matrix);

    }

    private static floatQuat FromTo(float3 from, float3 to) {

        var dot = float3.Dot(from, to);

        if (dot < -0.9999f) {

            return floatQuat.CreateFromAxisAngle(float3.UnitX, MathF.PI);

        }

        var cross = float3.Cross(from, to);

        return new floatQuat(cross.X, cross.Y, cross.Z, 1 + dot);

    }

    // Instances an existing model; its meshes are copied once and referenced by each transform.
    public void Append(ModelData source, params float4x4[] transforms) {

        var materialBase = _materials.Count;
        var textureBase = _textures.Count;

        foreach (var texture in source.Textures) {

            _textures.Add(texture);

        }

        foreach (var material in source.Materials) {

            _materials.Add(Remap(material, textureBase));

        }

        _sources.Add((source, materialBase, textureBase, transforms));

    }

    private static MaterialData Remap(MaterialData m, int textureBase) {

        int Shift(int index) => index >= 0 ? index + textureBase : -1;

        return new MaterialData {

            Name = m.Name,
            BaseColor = m.BaseColor,
            Metallic = m.Metallic,
            Roughness = m.Roughness,
            Emissive = m.Emissive,
            NormalScale = m.NormalScale,
            OcclusionStrength = m.OcclusionStrength,
            BaseColorTexture = Shift(m.BaseColorTexture),
            MetallicRoughnessTexture = Shift(m.MetallicRoughnessTexture),
            NormalTexture = Shift(m.NormalTexture),
            OcclusionTexture = Shift(m.OcclusionTexture),
            EmissiveTexture = Shift(m.EmissiveTexture),
            AlphaMode = m.AlphaMode,
            AlphaCutoff = m.AlphaCutoff,
            DoubleSided = m.DoubleSided,

        };

    }

    private List<uint> Indices(int material) {

        if (!_indices.TryGetValue(material, out var list)) {

            list = [];
            _indices[material] = list;

        }

        return list;

    }

    public ModelData Build(string name) {

        var vertices = new List<MeshVertex>(_vertices);
        var indices = new List<uint>();
        var primitives = new List<PrimitiveData>();
        var meshes = new List<MeshRange>();
        var instances = new List<MeshInstance>();

        if (_indices.Count > 0) {

            foreach (var (material, list) in _indices.OrderBy(pair => pair.Key)) {

                primitives.Add(new PrimitiveData(indices.Count, list.Count, material));
                indices.AddRange(list);

            }

            meshes.Add(new MeshRange(0, primitives.Count, name));
            instances.Add(new MeshInstance(0, float4x4.Identity));

        }

        foreach (var (source, materialBase, _, transforms) in _sources) {

            var vertexBase = (uint)vertices.Count;
            var meshBase = meshes.Count;

            vertices.AddRange(source.Vertices);

            foreach (var mesh in source.Meshes) {

                var first = primitives.Count;

                for (var p = mesh.FirstPrimitive; p < mesh.FirstPrimitive + mesh.PrimitiveCount; p++) {

                    var primitive = source.Primitives[p];
                    var start = indices.Count;

                    for (var i = primitive.FirstIndex; i < primitive.FirstIndex + primitive.IndexCount; i++) {

                        indices.Add(source.Indices[i] + vertexBase);

                    }

                    primitives.Add(new PrimitiveData(start, primitive.IndexCount, primitive.Material + materialBase));

                }

                meshes.Add(new MeshRange(first, primitives.Count - first, mesh.Name));

            }

            foreach (var transform in transforms) {

                foreach (var instance in source.Instances) {

                    instances.Add(new MeshInstance(meshBase + instance.Mesh, instance.Transform * transform));

                }

            }

        }

        var model = new ModelData {

            Name = name,
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            Primitives = primitives.ToArray(),
            Meshes = meshes.ToArray(),
            Instances = instances.ToArray(),
            Materials = _materials.ToArray(),
            Textures = _textures.ToArray(),

        };

        if (_indices.Count > 0) {

            var generated = primitives.Take(meshes[0].PrimitiveCount).ToArray();

            foreach (var primitive in generated) {

                MeshMath.ComputeTangents(model.Vertices.AsSpan(0, _vertices.Count), model.Indices.AsSpan(primitive.FirstIndex, primitive.IndexCount));

            }

        }

        model.ComputeBounds();

        return model;

    }

}
