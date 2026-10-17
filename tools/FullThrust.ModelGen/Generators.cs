using Brutal.Numerics;
using FullThrust.Assets;

namespace FullThrust.ModelGen;

// Procedural part models. Every part is centred on its origin, nose towards +Y, sized in metres.
public static class Generators {

    private const int SkinSize = 512;

    public static ModelData Build(Recipe recipe, string modelsRoot) {

        return recipe.Type.ToLowerInvariant() switch {

            "tank" => Tank(recipe),
            "pod" => Pod(recipe),
            "ring" => Ring(recipe),
            "rcs" => Rcs(recipe),
            "cluster" => Cluster(recipe, modelsRoot),
            "crop" => Crop(recipe, modelsRoot),
            "launchpad" => LaunchPad(recipe),
            _ => throw new InvalidDataException($"{recipe.Output}: unknown recipe type '{recipe.Type}'."),

        };

    }

    private static MaterialData Material(string name, float3 color, float metallic, float roughness, bool doubleSided = false) {

        return new MaterialData { Name = name, BaseColor = new float4(color, 1), Metallic = metallic, Roughness = roughness, DoubleSided = doubleSided };

    }

    // Painted skin with weld seams and stringers in the normal map; one tile spans the circumference.
    private static MaterialData Skin(MeshBuilder mesh, string name, Recipe recipe, float circumference, float tileMetres, float3 paint, float3 accent) {

        var texture = new TextureBuilder(SkinSize, SkinSize);
        var seams = Math.Max(1, (int)MathF.Round(tileMetres / 3.0f));
        var stringers = Math.Max(12, (int)MathF.Round(circumference / 0.6f));
        var radiators = 24;

        texture.Paint((u, v, t) => {

            var grime = Noise.Fbm(u, v, 4, 5, recipe.Seed);
            var fine = Noise.Fbm(u, v, 32, 3, recipe.Seed + 7);
            var color = paint;
            var metallic = (float)recipe.PaintMetallic;
            var roughness = (float)recipe.PaintRoughness;
            var height = 0f;

            switch (recipe.Pattern) {

                case "roll":
                    color = ((int)(u * 4) & 1) == 0 ? paint : accent;
                    break;

                case "radiator":
                    var strip = u * radiators % 1.0f;

                    if (strip > 0.55f) {

                        color = new float3(0.92f);
                        metallic = 0;
                        roughness = 0.55f;
                        height += 0.002f;

                    }

                    break;

                case "corrugated":
                    height += 0.012f * MathF.Sin(u * MathF.Tau * circumference / 0.25f);
                    break;

            }

            var seam = MathF.Abs(v * seams % 1.0f - 0.5f) * 2;
            var seamProfile = MathF.Exp(-MathF.Pow((1 - seam) * tileMetres / seams / 0.01f, 2));
            var stringer = MathF.Abs(u * stringers % 1.0f - 0.5f) * 2;
            var stringerProfile = MathF.Exp(-MathF.Pow((1 - stringer) * circumference / stringers / 0.008f, 2));

            height -= 0.0018f * seamProfile + 0.0006f * stringerProfile;
            height += 0.00015f * fine;

            t.Color = color * (0.93f + 0.1f * grime) * (1 - 0.25f * seamProfile);
            t.Roughness = Math.Clamp(roughness + 0.12f * (grime - 0.5f) + 0.15f * seamProfile, 0.05f, 1);
            t.Metallic = metallic;
            t.Height = height;
            t.Occlusion = 1 - 0.3f * seamProfile;

        });

        var material = Material(name, new float3(1), 1, 1);

        material.BaseColorTexture = mesh.AddTexture(texture.BaseColor(name + "_basecolor"));
        material.MetallicRoughnessTexture = mesh.AddTexture(texture.MetallicRoughness(name + "_orm"));
        material.OcclusionTexture = material.MetallicRoughnessTexture;
        material.NormalTexture = mesh.AddTexture(texture.Normal(name + "_normal", circumference / SkinSize, tileMetres / SkinSize));

        return material;

    }

    private static ModelData Tank(Recipe recipe) {

        var mesh = new MeshBuilder();
        var length = (float)recipe.Length;
        var radius = (float)recipe.Diameter / 2;
        var circumference = MathF.Tau * radius;
        var y0 = -length / 2;

        var paint = mesh.AddMaterial(Skin(mesh, "paint", recipe, circumference, circumference, recipe.PaintColor, recipe.AccentColor));
        var accentMaterial = Skin(mesh, "accent", new Recipe { Pattern = "solid", Seed = recipe.Seed + 3, PaintRoughness = 0.5 }, circumference, circumference, recipe.AccentColor, recipe.AccentColor);
        var accent = mesh.AddMaterial(accentMaterial);
        var metal = mesh.AddMaterial(Material("metal", new float3(0.75f, 0.76f, 0.78f), 1, 0.32f));
        var dark = mesh.AddMaterial(Material("bulkhead", new float3(0.12f), 0.6f, 0.55f));

        var options = LatheOptions.Default with { Segments = 96, VMetres = circumference, SmoothAngleDeg = 10 };

        foreach (var (from, to, material) in WallSegments(recipe, length, paint, accent, metal)) {

            mesh.Lathe([new(radius, y0 + from), new(radius, y0 + to)], material, options);

        }

        var inset = radius * 0.96f;

        mesh.Lathe([new(0, y0 + 0.01f), new(inset, y0 + 0.01f), new(inset, y0), new(radius, y0)], dark, options with { SmoothAngleDeg = 1 });
        mesh.Lathe([new(radius, -y0), new(inset, -y0), new(inset, -y0 - 0.01f), new(0, -y0 - 0.01f)], dark, options with { SmoothAngleDeg = 1 });

        if (recipe.Raceway && length > 2) {

            var width = MathF.Max(0.08f, radius * 0.07f);
            var depth = width * 0.6f;

            mesh.Box(new float3(-width / 2, y0 + length * 0.04f, -radius - depth), new float3(width / 2, -y0 - length * 0.04f, -radius + 0.02f), paint, float4x4.CreateRotationY(MathF.PI / 2), circumference);

        }

        return mesh.Build(recipe.Output);

    }

    private static IEnumerable<(float From, float To, int Material)> WallSegments(Recipe recipe, float length, int paint, int accent, int metal) {

        var cursor = 0f;

        foreach (var band in recipe.Bands.OrderBy(b => b.From)) {

            var from = Math.Clamp((float)band.From, 0, length);
            var to = Math.Clamp((float)band.To, from, length);

            if (from > cursor) {

                yield return (cursor, from, paint);

            }

            if (to > from) {

                yield return (from, to, band.Material == "metal" ? metal : accent);

            }

            cursor = Math.Max(cursor, to);

        }

        if (cursor < length) {

            yield return (cursor, length, paint);

        }

    }

    private static ModelData Pod(Recipe recipe) {

        var mesh = new MeshBuilder();
        var length = (float)recipe.Length;
        var radius = (float)recipe.Diameter / 2;
        var top = (float)recipe.TopDiameter / 2;
        var shield = (float)recipe.HeatShieldDepth;
        var nose = (float)recipe.NoseHeight;
        var tunnel = recipe.DockingTunnel ? MathF.Min(0.5f, length * 0.15f) : 0;
        var y0 = -length / 2;
        var y1 = length / 2;

        var bodyBottom = y0 + shield + 0.06f;
        var bodyTop = y1 - MathF.Max(nose, tunnel);
        var slant = float2.Distance(new float2(radius * 0.985f, bodyBottom), new float2(top, bodyTop));

        var body = mesh.AddMaterial(PodSkin(mesh, recipe, MathF.Tau * radius, slant));
        var heat = mesh.AddMaterial(Material("heatshield", new float3(0.11f, 0.085f, 0.07f), 0, 0.88f));
        var metal = mesh.AddMaterial(Material("metal", new float3(0.7f), 1, 0.35f));

        var options = LatheOptions.Default with { Segments = 96, VMetres = slant, SmoothAngleDeg = 25 };

        mesh.Lathe([new(0, y0), new(radius * 0.55f, y0 + shield * 0.18f), new(radius * 0.88f, y0 + shield * 0.55f), new(radius, y0 + shield), new(radius * 0.985f, bodyBottom)], heat, options with { SmoothAngleDeg = 40 });
        mesh.Lathe([new(radius * 0.985f, bodyBottom), new(top, bodyTop)], body, options);

        if (recipe.DockingTunnel) {

            var tunnelRadius = MathF.Min(top * 0.8f, 0.5f);

            mesh.Lathe([new(top, bodyTop), new(tunnelRadius, bodyTop)], body, options with { SmoothAngleDeg = 1 });
            mesh.Lathe([new(tunnelRadius, bodyTop), new(tunnelRadius, y1 - 0.12f), new(tunnelRadius * 0.85f, y1 - 0.08f), new(0.12f, y1 - 0.08f)], metal, options with { SmoothAngleDeg = 35 });
            mesh.Lathe([new(0.12f, y1 - 0.08f), new(0.03f, y1), new(0, y1)], metal, options);

        } else if (nose > 0) {

            mesh.Lathe([new(top, bodyTop), new(top * 0.92f, bodyTop + nose * 0.45f), new(top * 0.62f, bodyTop + nose * 0.85f), new(0, y1)], body, options with { SmoothAngleDeg = 60 });

        } else {

            mesh.Lathe([new(top, bodyTop), new(0, bodyTop)], body, options);

        }

        return mesh.Build(recipe.Output);

    }

    // Capsule sidewall with windows and panel lines; V spans the slant height exactly once.
    private static MaterialData PodSkin(MeshBuilder mesh, Recipe recipe, float circumference, float slant) {

        var texture = new TextureBuilder(SkinSize, SkinSize);
        var paint = recipe.PaintColor;
        var windows = recipe.Windows;

        texture.Paint((u, v, t) => {

            var height = 1 - v;
            var grime = Noise.Fbm(u, v, 6, 4, recipe.Seed);
            var color = paint * (0.94f + 0.08f * grime);
            var roughness = (float)recipe.PaintRoughness + 0.1f * (grime - 0.5f);
            var metallic = (float)recipe.PaintMetallic;
            var relief = 0f;

            var panelU = MathF.Abs(u * 12 % 1.0f - 0.5f) * 2;
            var panelV = MathF.Abs(height * 4 % 1.0f - 0.5f) * 2;
            var line = MathF.Exp(-MathF.Pow((1 - panelU) * circumference / 12 / 0.006f, 2)) + MathF.Exp(-MathF.Pow((1 - panelV) * slant / 4 / 0.006f, 2));

            relief -= 0.001f * line;
            color *= 1 - 0.15f * MathF.Min(1, line);

            for (var w = 0; w < windows; w++) {

                var centre = (w + 0.5f) / windows;
                var du = (u - centre) * circumference;
                var dv = (height - 0.62f) * slant;
                var d = RoundedBox(du, dv, 0.16f, 0.2f, 0.06f);

                if (d < 0.03f) {

                    color = new float3(0.06f);
                    metallic = 0.3f;
                    roughness = 0.5f;

                }

                if (d < 0) {

                    color = new float3(0.015f, 0.02f, 0.03f);
                    metallic = 0;
                    roughness = 0.04f;
                    relief -= 0.004f;

                }

            }

            t.Color = color;
            t.Roughness = Math.Clamp(roughness, 0.03f, 1);
            t.Metallic = metallic;
            t.Height = relief;

        });

        var material = Material("capsule", new float3(1), 1, 1);

        material.BaseColorTexture = mesh.AddTexture(texture.BaseColor("capsule_basecolor"));
        material.MetallicRoughnessTexture = mesh.AddTexture(texture.MetallicRoughness("capsule_orm"));
        material.NormalTexture = mesh.AddTexture(texture.Normal("capsule_normal", circumference / SkinSize, slant / SkinSize));

        return material;

    }

    private static float RoundedBox(float x, float y, float halfWidth, float halfHeight, float radius) {

        var qx = MathF.Abs(x) - halfWidth + radius;
        var qy = MathF.Abs(y) - halfHeight + radius;

        return MathF.Sqrt(MathF.Max(qx, 0) * MathF.Max(qx, 0) + MathF.Max(qy, 0) * MathF.Max(qy, 0)) + MathF.Min(MathF.Max(qx, qy), 0) - radius;

    }

    // Stack decouplers, interstages and adapters: a short shell with a separation groove and bolt rows.
    private static ModelData Ring(Recipe recipe) {

        var mesh = new MeshBuilder();
        var length = (float)recipe.Length;
        var radius = (float)recipe.Diameter / 2;
        var circumference = MathF.Tau * radius;
        var y0 = -length / 2;
        var y1 = length / 2;

        var texture = new TextureBuilder(SkinSize, 64);
        var bolts = Math.Max(16, (int)(circumference / 0.12f));
        var paint = recipe.PaintColor;

        texture.Paint((u, v, t) => {

            var height = 1 - v;
            var bu = (u * bolts % 1.0f - 0.5f) * circumference / bolts;
            var relief = 0f;

            foreach (var row in (ReadOnlySpan<float>)[0.12f, 0.88f]) {

                var bv = (height - row) * length;
                var r = MathF.Sqrt(bu * bu + bv * bv);

                relief += 0.006f * MathF.Max(0, 1 - r / 0.018f);

            }

            if (recipe.Pattern == "corrugated") {

                relief += 0.01f * MathF.Sin(u * MathF.Tau * circumference / 0.25f);

            }

            var grime = Noise.Fbm(u, v, 8, 3, recipe.Seed);

            t.Color = paint * (0.92f + 0.12f * grime);
            t.Roughness = (float)recipe.PaintRoughness + 0.1f * (grime - 0.5f);
            t.Metallic = (float)recipe.PaintMetallic;
            t.Height = relief;

        });

        var skin = Material("shell", new float3(1), 1, 1);

        skin.BaseColorTexture = mesh.AddTexture(texture.BaseColor("shell_basecolor"));
        skin.MetallicRoughnessTexture = mesh.AddTexture(texture.MetallicRoughness("shell_orm"));
        skin.NormalTexture = mesh.AddTexture(texture.Normal("shell_normal", circumference / texture.Width, length / texture.Height));

        var shell = mesh.AddMaterial(skin);
        var dark = mesh.AddMaterial(Material("bulkhead", recipe.AccentColor, 0.5f, 0.6f));
        var groove = radius - MathF.Max(0.01f, radius * 0.012f);
        var options = LatheOptions.Default with { Segments = 96, VMetres = length, SmoothAngleDeg = 5 };

        mesh.Lathe([new(0, y0), new(radius, y0)], dark, options);
        mesh.Lathe([new(radius, y0), new(radius, -0.012f), new(groove, -0.006f), new(groove, 0.006f), new(radius, 0.012f), new(radius, y1)], shell, options);
        mesh.Lathe([new(radius, y1), new(0, y1)], dark, options);

        return mesh.Build(recipe.Output);

    }

    // A thruster block on a mount plate, with four nozzles at the outboard end (+X).
    private static ModelData Rcs(Recipe recipe) {

        var mesh = new MeshBuilder();
        var housing = mesh.AddMaterial(Material("housing", recipe.PaintColor, 0.2f, 0.5f));
        var metal = mesh.AddMaterial(Material("mount", new float3(0.55f), 1, 0.4f));
        var nozzle = mesh.AddMaterial(Material("nozzle", new float3(0.22f, 0.2f, 0.19f), 1, 0.45f, doubleSided: true));

        var halfY = (float)recipe.Length / 2;
        var halfXZ = (float)recipe.Diameter / 2;

        mesh.Box(new float3(-halfXZ, -halfY * 0.85f, -halfXZ), new float3(-halfXZ + 0.03f, halfY * 0.85f, halfXZ), metal);
        mesh.Box(new float3(-halfXZ + 0.03f, -halfY * 0.6f, -halfXZ * 0.75f), new float3(halfXZ * 0.45f, halfY * 0.6f, halfXZ * 0.75f), housing);

        var tip = halfXZ * 0.3f;

        Span<(float3 Position, float3 Direction)> nozzles = [

            (new float3(tip, halfY * 0.6f, 0), float3.UnitY),
            (new float3(tip, -halfY * 0.6f, 0), -float3.UnitY),
            (new float3(tip, 0, halfXZ * 0.75f), float3.UnitZ),
            (new float3(tip, 0, -halfXZ * 0.75f), -float3.UnitZ),

        ];

        var scale = halfY / 0.25f;

        foreach (var (position, direction) in nozzles) {

            var rotation = float4x4.CreateFromQuaternion(FromTo(float3.UnitY, direction));
            var matrix = float4x4.CreateScale(scale) * rotation * float4x4.CreateTranslation(position);

            mesh.Lathe([new(0.016f, 0), new(0.022f, 0.02f), new(0.034f, 0.065f), new(0.04f, 0.085f)], nozzle, LatheOptions.Default with { Segments = 20 }, matrix);

        }

        return mesh.Build(recipe.Output);

    }

    private static floatQuat FromTo(float3 from, float3 to) {

        var dot = float3.Dot(from, to);

        if (dot < -0.9999f) {

            return floatQuat.CreateFromAxisAngle(float3.UnitX, MathF.PI);

        }

        var cross = float3.Cross(from, to);

        return floatQuat.Normalize(new floatQuat(cross.X, cross.Y, cross.Z, 1 + dot));

    }

    // A thrust plate with a ring of engines (and optionally one in the centre) instanced from a source model.
    private static ModelData Cluster(Recipe recipe, string modelsRoot) {

        var mesh = new MeshBuilder();
        var length = (float)recipe.Length;
        var radius = (float)recipe.Diameter / 2;
        var y1 = length / 2;
        var plate = length - (float)recipe.EngineLength;

        var structure = mesh.AddMaterial(Material("thrust_structure", new float3(0.16f), 0.7f, 0.5f));
        var shield = mesh.AddMaterial(Material("heat_shield", new float3(0.3f, 0.29f, 0.27f), 0, 0.85f));
        var options = LatheOptions.Default with { Segments = 96, SmoothAngleDeg = 5 };

        mesh.Lathe([new(0, y1 - plate), new(radius * 0.97f, y1 - plate), new(radius, y1 - plate + 0.06f)], shield, options);
        mesh.Lathe([new(radius, y1 - plate + 0.06f), new(radius, y1), new(0, y1)], structure, options);

        var source = GltfModelLoader.Load(Path.Combine(modelsRoot, recipe.Source!));
        var fit = FitEngine(source, recipe);
        var transforms = new List<float4x4>();
        var engineTop = y1 - plate;

        if (recipe.CenterEngine) {

            transforms.Add(fit * float4x4.CreateTranslation(0, engineTop - (float)recipe.EngineLength / 2, 0));

        }

        for (var i = 0; i < recipe.RingEngines; i++) {

            var angle = MathF.Tau * i / recipe.RingEngines;
            var (sin, cos) = MathF.SinCos(angle);
            var ring = (float)recipe.RingRadius;

            transforms.Add(fit * float4x4.CreateTranslation(cos * ring, engineTop - (float)recipe.EngineLength / 2, -sin * ring));

        }

        mesh.Append(source, transforms.ToArray());

        return mesh.Build(recipe.Output);

    }

    // Orients the source, centres it and stretches it to the engine's diameter and length.
    private static float4x4 FitEngine(ModelData source, Recipe recipe) {

        var degrees = recipe.SourceRotationDeg;
        var rotation = float4x4.CreateRotationX((float)(degrees[0] * Math.PI / 180)) * float4x4.CreateRotationY((float)(degrees[1] * Math.PI / 180)) * float4x4.CreateRotationZ((float)(degrees[2] * Math.PI / 180));

        var min = new float3(float.MaxValue);
        var max = new float3(float.MinValue);

        foreach (var instance in source.Instances) {

            var transform = instance.Transform * rotation;

            foreach (var vertex in source.Vertices) {

                var p = float3.Transform(vertex.Position, transform);

                min = float3.Min(min, p);
                max = float3.Max(max, p);

            }

        }

        var size = max - min;
        var target = new float3((float)recipe.EngineDiameter, (float)recipe.EngineLength, (float)recipe.EngineDiameter);

        return rotation * float4x4.CreateTranslation(-(min + max) * 0.5f) * float4x4.CreateScale(target / size);

    }

    // Keeps the triangles whose centroids fall inside a Y range of the source (after node transforms).
    private static ModelData Crop(Recipe recipe, string modelsRoot) {

        var source = GltfModelLoader.Load(Path.Combine(modelsRoot, recipe.Source!));

        var vertices = new List<MeshVertex>();
        var indices = new List<uint>();
        var primitives = new List<PrimitiveData>();
        var meshes = new List<MeshRange>();
        var instances = new List<MeshInstance>();

        foreach (var instance in source.Instances) {

            var range = source.Meshes[instance.Mesh];
            var remap = new Dictionary<uint, uint>();
            var first = primitives.Count;

            for (var p = range.FirstPrimitive; p < range.FirstPrimitive + range.PrimitiveCount; p++) {

                var primitive = source.Primitives[p];
                var start = indices.Count;

                for (var i = primitive.FirstIndex; i + 2 < primitive.FirstIndex + primitive.IndexCount; i += 3) {

                    var centroid = 0f;

                    for (var k = 0; k < 3; k++) {

                        centroid += float3.Transform(source.Vertices[source.Indices[i + k]].Position, instance.Transform).Y / 3;

                    }

                    if (centroid < recipe.CropMinY || centroid > recipe.CropMaxY) {

                        continue;

                    }

                    for (var k = 0; k < 3; k++) {

                        var index = source.Indices[i + k];

                        if (!remap.TryGetValue(index, out var mapped)) {

                            mapped = (uint)vertices.Count;
                            remap[index] = mapped;
                            vertices.Add(source.Vertices[index]);

                        }

                        indices.Add(mapped);

                    }

                }

                if (indices.Count > start) {

                    primitives.Add(new PrimitiveData(start, indices.Count - start, primitive.Material));

                }

            }

            if (primitives.Count > first) {

                meshes.Add(new MeshRange(first, primitives.Count - first, range.Name));
                instances.Add(new MeshInstance(meshes.Count - 1, instance.Transform));

            }

        }

        var model = new ModelData {

            Name = recipe.Output,
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            Primitives = primitives.ToArray(),
            Meshes = meshes.ToArray(),
            Instances = instances.ToArray(),
            Materials = source.Materials,
            Textures = source.Textures,

        };

        model.ComputeBounds();

        return model;

    }

    // Concrete apron flush with y = 0 plus a service tower and water tower; Diameter is the apron width.
    private static ModelData LaunchPad(Recipe recipe) {

        var mesh = new MeshBuilder();
        var size = (float)recipe.Diameter;
        var depth = (float)recipe.Length;
        var half = size / 2;

        var texture = new TextureBuilder(1024, 1024);
        var joints = (int)MathF.Round(size / 7.5f);

        texture.Paint((u, v, t) => {

            var x = (u - 0.5f) * size;
            var z = (v - 0.5f) * size;
            var r = MathF.Sqrt(x * x + z * z);

            var grime = Noise.Fbm(u, v, 8, 6, recipe.Seed);
            var stains = Noise.Fbm(u, v, 3, 4, recipe.Seed + 11);
            var color = new float3(0.36f, 0.355f, 0.335f) * (0.82f + 0.3f * grime) * (0.85f + 0.2f * stains);
            var roughness = 0.88f - 0.1f * stains;

            var ju = MathF.Abs(u * joints % 1.0f - 0.5f) * 2;
            var jv = MathF.Abs(v * joints % 1.0f - 0.5f) * 2;
            var joint = MathF.Max(MathF.Exp(-MathF.Pow((1 - ju) * size / joints / 0.05f, 2)), MathF.Exp(-MathF.Pow((1 - jv) * size / joints / 0.05f, 2)));

            color *= 1 - 0.45f * joint;

            var scorch = MathF.Max(0, 1 - r / 26) * (0.7f + 0.3f * stains);

            color *= 1 - 0.75f * scorch * scorch;

            if (MathF.Abs(z) < 4.5f && x > 6 && x < half - 4) {

                color = new float3(0.035f) * (0.8f + 0.4f * grime);
                roughness = 0.95f;

            }

            if (MathF.Abs(r - 8) < 0.15f) {

                color = new float3(0.75f, 0.58f, 0.05f) * (1 - 0.6f * scorch);

            }

            t.Color = color;
            t.Roughness = roughness;
            t.Metallic = 0;
            t.Height = -0.02f * joint + 0.002f * grime;

        });

        var concrete = Material("concrete", new float3(1), 1, 1);

        concrete.BaseColorTexture = mesh.AddTexture(texture.BaseColor("concrete_basecolor"));
        concrete.MetallicRoughnessTexture = mesh.AddTexture(texture.MetallicRoughness("concrete_orm"));
        concrete.NormalTexture = mesh.AddTexture(texture.Normal("concrete_normal", size / texture.Width, size / texture.Height));

        var apron = mesh.AddMaterial(concrete);
        var steel = mesh.AddMaterial(Material("tower_steel", new float3(0.55f, 0.16f, 0.08f), 0.3f, 0.55f));
        var white = mesh.AddMaterial(Material("water_tank", new float3(0.85f, 0.85f, 0.82f), 0.2f, 0.4f));
        var mast = mesh.AddMaterial(Material("mast", new float3(0.7f), 1, 0.3f));

        mesh.Box(new float3(-half, -depth, -half), new float3(half, 0, half), apron, uvMetres: size);

        var tower = new float3(-22, 0, 0);

        BuildTower(mesh, tower, 8, 72, steel);
        mesh.Cylinder(0.25f, 72, 92, mast, 12, float4x4.CreateTranslation(tower));

        BuildWaterTower(mesh, new float3(-38, 0, 34), white, steel);

        return mesh.Build(recipe.Output);

    }

    private static void BuildTower(MeshBuilder mesh, float3 origin, float width, float height, int material) {

        var h = width / 2;
        var levels = (int)(height / 6);

        Span<float3> corners = [new(-h, 0, -h), new(h, 0, -h), new(h, 0, h), new(-h, 0, h)];

        for (var c = 0; c < 4; c++) {

            mesh.Strut(origin + corners[c], origin + corners[c] + new float3(0, height, 0), 0.5f, material);

        }

        for (var level = 1; level <= levels; level++) {

            var y = height * level / levels;
            var below = height * (level - 1) / levels;

            for (var c = 0; c < 4; c++) {

                var a = corners[c];
                var b = corners[(c + 1) % 4];

                mesh.Strut(origin + a + new float3(0, y, 0), origin + b + new float3(0, y, 0), 0.3f, material);
                mesh.Strut(origin + a + new float3(0, below, 0), origin + b + new float3(0, y, 0), 0.18f, material);

            }

        }

    }

    private static void BuildWaterTower(MeshBuilder mesh, float3 origin, int tank, int legs) {

        var options = LatheOptions.Default with { Segments = 48, SmoothAngleDeg = 50 };
        var top = 24f;

        Span<float3> feet = [new(-4, 0, -4), new(4, 0, -4), new(4, 0, 4), new(-4, 0, 4)];

        foreach (var foot in feet) {

            mesh.Strut(origin + foot, origin + foot * 0.4f + new float3(0, top, 0), 0.5f, legs);

        }

        var bowl = float4x4.CreateTranslation(origin + new float3(0, top, 0));

        mesh.Lathe([new(0, -1.5f), new(3.5f, 0), new(6, 2.5f), new(6, 6), new(4.5f, 7.5f), new(0, 8.2f)], tank, options, bowl);

    }

}
