using Brutal.Numerics;
using FullThrust.Assets;
using FullThrust.Core.Rendering;

namespace FullThrust.ModelGen;

// Procedural part models. Every part is centred on its origin, nose towards +Y, sized in metres.
public static class Generators {

    private const int SkinSize = 512;

    public static ModelData Build(Recipe recipe, string modelsRoot) {

        return recipe.Type.ToLowerInvariant() switch {

            "tank" => Tank(recipe),
            "pod" => Pod(recipe),
            "ring" => Ring(recipe),
            "shroud" => Shroud(recipe),
            "rcs" => Rcs(recipe),
            "cooled-engine" => CooledEngine(recipe),
            "cluster" => Cluster(recipe, modelsRoot),
            "crop" => Crop(recipe, modelsRoot),
            "launchpad" => LaunchPad(recipe),
            "grove" => CoastalGrove.Build(recipe),
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

                case "tracking":
                    var markX = ((u * 4) % 1 - 0.5f) * circumference / 4;
                    var markY = (v - 0.32f) * tileMetres;

                    if (MathF.Max(MathF.Abs(markX), MathF.Abs(markY)) < 0.29f && (markX < 0) == (markY < 0)) {

                        color = accent;
                        roughness = 0.55f;

                    }

                    break;

                case "corrugated":
                    height += 0.012f * MathF.Sin(u * MathF.Tau * MathF.Round(circumference / 0.25f));
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

            for (var station = y0 + length * 0.06f; station < -y0 - length * 0.04f; station += 2.4f) {

                mesh.Box(new float3(-width * 0.65f, station, -radius - depth - 0.014f), new float3(width * 0.65f, station + 0.055f, -radius + 0.02f), metal, float4x4.CreateRotationY(MathF.PI / 2));

            }

            for (var side = 0; side < 3; side++) {

                var angle = side * MathF.Tau / 3;
                var height = y0 + length * 0.17f;
                var mount = float4x4.CreateRotationZ(-MathF.PI / 2) * float4x4.CreateTranslation(radius - 0.032f, height, 0) * float4x4.CreateRotationY(angle);
                var port = MathF.Min(radius * 0.18f, 0.32f);

                mesh.Cylinder(port, 0, 0.075f, metal, 32, mount);
                mesh.Cylinder(port * 0.77f, 0.075f, 0.082f, dark, 32, mount);

            }

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

    private static ModelData Shroud(Recipe recipe) {

        var mesh = new MeshBuilder();
        var skin = mesh.AddMaterial(Skin(mesh, "fluted_shell", recipe, MathF.PI, 1, recipe.PaintColor, recipe.AccentColor));
        var liner = mesh.AddMaterial(Material("inner_liner", new float3(0.17f, 0.16f, 0.14f), 0.70f, 0.56f));
        var options = LatheOptions.Default with {

            Segments = 96,
            SmoothAngleDeg = 12,

        };

        mesh.Lathe([new(0.495f, -0.5f), new(0.5f, -0.48f), new(0.5f, 0.47f), new(0.49f, 0.5f)], skin, options);
        mesh.Lathe([new(0.49f, 0.5f), new(0.48f, 0.5f), new(0.48f, -0.5f), new(0.495f, -0.5f)], liner, options);

        return mesh.Build(recipe.Output);

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

        var bodyBottom = shield == 0 ? y0 : y0 + shield + 0.06f;
        var baseRadius = shield == 0 ? radius : radius * 0.985f;
        var bodyTop = y1 - MathF.Max(nose, tunnel);
        var slant = float2.Distance(new float2(baseRadius, bodyBottom), new float2(top, bodyTop));

        var body = mesh.AddMaterial(PodSkin(mesh, recipe, MathF.Tau * radius, slant));
        var cap = mesh.AddMaterial(Material("forward_cover", recipe.PaintColor * 0.89f, (float)recipe.PaintMetallic, 0.46f));
        var heat = mesh.AddMaterial(Ablator(mesh, recipe.Seed, radius));
        var metal = mesh.AddMaterial(Material("metal", new float3(0.7f), 1, 0.35f));

        var options = LatheOptions.Default with { Segments = 96, VMetres = slant, SmoothAngleDeg = 25 };

        float2[] shieldProfile = shield == 0
            ? [new(0, y0), new(radius, y0)]
            : [new(0, y0), new(radius * 0.55f, y0 + shield * 0.18f), new(radius * 0.88f, y0 + shield * 0.55f), new(radius, y0 + shield), new(baseRadius, bodyBottom)];
        mesh.Lathe(shieldProfile, heat, options with {

            SmoothAngleDeg = 40,
            VMetres = radius * 1.17f,

        });
        mesh.Lathe([new(baseRadius, bodyBottom), new(top, bodyTop)], body, options);

        if (recipe.DockingTunnel) {

            var tunnelRadius = MathF.Min(top * 0.8f, 0.5f);

            mesh.Lathe([new(top, bodyTop), new(tunnelRadius, bodyTop)], cap, options with { SmoothAngleDeg = 1 });
            mesh.Lathe([new(tunnelRadius, bodyTop), new(tunnelRadius, y1 - 0.12f), new(tunnelRadius * 0.85f, y1 - 0.08f), new(0.12f, y1 - 0.08f)], metal, options with { SmoothAngleDeg = 35 });
            mesh.Lathe([new(0.12f, y1 - 0.08f), new(0.03f, y1), new(0, y1)], metal, options);

        } else if (nose > 0) {

            var cover = new float2[13];

            for (var ring = 0; ring < cover.Length; ring++) {

                var angle = ring * (MathF.PI * 0.5f) / (cover.Length - 1);

                cover[ring] = new float2(top * MathF.Cos(angle), bodyTop + nose * MathF.Sin(angle));

            }

            mesh.Lathe(cover, cap, options);

        } else {

            mesh.Lathe([new(top, bodyTop), new(0, bodyTop)], cap, options);

        }

        return mesh.Build(recipe.Output);

    }

    // Capsule sidewall with windows and panel lines; V spans the slant height exactly once.
    private static MaterialData PodSkin(MeshBuilder mesh, Recipe recipe, float circumference, float slant) {

        var texture = new TextureBuilder(1024, SkinSize);
        var paint = recipe.PaintColor;
        var windows = recipe.Windows;

        texture.Paint((u, v, t) => {

            var height = 1 - v;
            var grime = Noise.Fbm(u, v, 6, 4, recipe.Seed);
            var panel = Noise.Hash((int)(u * 12), (int)(height * 4), 12, recipe.Seed);
            var color = paint * (0.81f + 0.09f * panel + 0.13f * grime);
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
                var d = RoundedBox(du, dv, 0.28f + 0.08f * dv, 0.21f, 0.07f);

                if (w % 2 == 0) {

                    var hatch = RoundedBox(du, dv + 0.19f, 0.53f, 0.63f, 0.17f);
                    var gasket = Math.Clamp(1 - MathF.Abs(hatch) / 0.024f, 0, 1);

                    color *= (hatch < 0 ? 0.90f : 1) * (1 - 0.82f * gasket);
                    relief += (hatch < 0 ? 0.006f : 0) - 0.005f * gasket;

                    if (MathF.Abs(du - 0.46f) < 0.045f && MathF.Abs(dv + 0.23f) < 0.12f) {

                        color = new float3(0.57f, 0.055f, 0.018f);
                        metallic = 0.20f;
                        relief += 0.018f;

                    }

                    var labelX = du - 0.61f;
                    var labelY = -0.23f - dv;

                    if (labelX >= 0 && labelX < 0.78f && MathF.Abs(labelY) < MathF.Min(0.094f, labelX * 0.70f)) {

                        color = RescueInk(labelX, labelY) ? new float3(0.018f) : new float3(0.90f, 0.58f, 0.075f);
                        metallic = 0;
                        roughness = 0.54f;

                    }

                    var hingeDistance = MathF.Min(MathF.Abs(dv - 0.09f), MathF.Abs(dv + 0.47f));
                    if (MathF.Abs(du + 0.52f) < 0.045f && hingeDistance < 0.076f) {

                        color = new float3(0.34f, 0.36f, 0.37f);
                        metallic = 0.82f;
                        roughness = 0.42f;
                        relief += 0.014f;

                    }

                }

                if (d < 0.075f) {

                    color = new float3(0.44f, 0.46f, 0.48f);
                    roughness = 0.35f;
                    metallic = 0.85f;
                    relief += 0.014f;

                }

                if (d < 0.03f) {

                    color = new float3(0.06f);
                    metallic = 0.3f;
                    roughness = 0.5f;

                }

                if (d < 0) {

                    color = new float3(0.026f, 0.044f, 0.065f);
                    metallic = 0.25f;
                    roughness = 0.09f;
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
        material.NormalTexture = mesh.AddTexture(texture.Normal("capsule_normal", circumference / texture.Width, slant / texture.Height));

        return material;

    }

    private static readonly string[] RescueStencil = [

        "####..#####..####..####.#...#.#####",
        "#...#.#.....#.....#.....#...#.#....",
        "#...#.#.....#.....#.....#...#.#....",
        "####..####...###..#.....#...#.####.",
        "#.#...#.........#.#.....#...#.#....",
        "#..#..#.........#.#.....#...#.#....",
        "#...#.#####.####...####..###..#####",

    ];

    private static bool RescueInk(float x, float y) {

        var col = (int)MathF.Floor((x - 0.15f) / 0.0175f);
        var row = (int)MathF.Floor((y + 0.063f) / 0.018f);

        return (uint)row < RescueStencil.Length && (uint)col < RescueStencil[row].Length && RescueStencil[row][col] == '#';

    }

    private static MaterialData Ablator(MeshBuilder mesh, int seed, float radius) {

        var texture = new TextureBuilder(512, 192);
        texture.Paint((u, v, texel) => {

            var radial = 1 - v;
            var theta = u * MathF.Tau;
            var x = MathF.Cos(theta) * radial;
            var y = MathF.Sin(theta) * radial;
            var resin = Noise.Fbm(x, y, 5, 5, seed + 19);
            var grain = Noise.Fbm(x, y, 53, 3, seed + 31);
            var flow = Noise.Fbm(u, radial * 0.16f, 42, 3, seed + 7);
            var charred = Math.Clamp(0.3f + 0.45f * radial + 0.80f * (flow - 0.5f) * radial + 0.45f * (resin - 0.5f), 0, 1);
            texel.Color = float3.Lerp(new float3(0.23f, 0.13f, 0.062f), new float3(0.037f, 0.032f, 0.029f), charred);
            texel.Color *= 0.70f + grain * 0.58f;
            texel.Roughness = 0.78f + 0.19f * grain;
            texel.Height = (grain - 0.5f) * 0.003f + flow * radial * 0.004f;

        });
        var material = Material("phenolic_ablator", new float3(1), 0, 1);
        material.BaseColorTexture = mesh.AddTexture(texture.BaseColor("ablator_basecolor"));
        material.NormalTexture = mesh.AddTexture(texture.Normal("ablator_normal", MathF.Tau * radius / texture.Width, radius / texture.Height));
        material.MetallicRoughnessTexture = mesh.AddTexture(texture.MetallicRoughness("ablator_orm"));

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

    private static ModelData CooledEngine(Recipe recipe) {

        var mesh = new MeshBuilder();
        var radius = (float)recipe.EngineDiameter * 0.5f;
        var length = (float)recipe.EngineLength;
        var top = length * 0.5f;
        var vacuum = recipe.VacuumExtension;
        var bellHeight = length * (vacuum ? 0.72f : 0.49f);
        var chamber = bellHeight - top;
        var throat = vacuum ? 0.11f : 0.28f;
        var shell = mesh.AddMaterial(Material("nozzle jacket", new float3(0.28f, 0.24f, 0.21f), 0.8f, 0.4f));
        var liner = mesh.AddMaterial(Material("fired liner", new float3(0.085f, 0.065f, 0.055f), 0.45f, 0.76f));
        var manifold = mesh.AddMaterial(Material("coolant manifolds", new float3(0.44f, 0.39f, 0.33f), 0.85f, 0.32f));
        var options = LatheOptions.Default with { Segments = 96 };
        var profile = new float2[25];
        var inner = new float2[profile.Length + 2];

        for (var i = 0; i < profile.Length; i++) {

            var t = i / (float)(profile.Length - 1);
            var r = radius * (1 - (1 - throat) * MathF.Pow(t, vacuum ? 1.48f : 1.85f));
            var y = -top + t * bellHeight;
            profile[i] = new float2(r, y);
            inner[profile.Length - i] = new float2(r - radius * 0.022f, y);

            if (i < profile.Length - 1 && i % (vacuum && t < 0.65f ? 6 : 2) == 0) {

                mesh.Lathe([new(r, y), new(r + radius * 0.012f, y + radius * 0.009f), new(r, y + radius * 0.025f)], manifold, options);

            }

        }

        inner[0] = new float2(0, chamber);
        inner[^1] = profile[0];
        mesh.Lathe(profile, shell, options);
        mesh.Lathe(inner, liner, options);

        var head = LatheOptions.Default with { Segments = 64 };
        var headRadius = radius * throat;
        mesh.Lathe([new(headRadius, chamber), new(headRadius * 1.55f, chamber + length * 0.055f), new(headRadius * 1.55f, top * 0.76f), new(headRadius * 1.8f, top * 0.78f), new(headRadius * 1.8f, top * 0.82f), new(headRadius, top * 0.84f), new(headRadius, top), new(0, top)], shell, head);

        var pumpRadius = headRadius * 0.85f;
        var pumpX = headRadius * 2.55f;
        var pump = float4x4.CreateTranslation(pumpX, 0, 0);
        mesh.Cylinder(pumpRadius, top * 0.58f, top * 0.88f, shell, 40, pump);
        mesh.Cylinder(pumpRadius * 1.12f, top * 0.7f, top * 0.73f, manifold, 40, pump);
        mesh.Strut(new float3(pumpX, top * 0.8f, 0), new float3(headRadius, top * 0.8f, 0), pumpRadius * 0.48f, manifold);

        var station = profile[vacuum ? 17 : 11];

        for (var i = 0; i < 2; i++) {

            var side = i == 0 ? -1f : 1f;
            var inlet = new float3(pumpX, top * 0.62f, 0);
            var elbow = new float3(pumpX, chamber + length * 0.04f, side * station.X * 0.65f);
            var outlet = new float3(station.X * 0.6f, station.Y, side * station.X * 0.8f);
            mesh.Strut(inlet, elbow, headRadius * 0.21f, manifold);
            mesh.Strut(elbow, outlet, headRadius * 0.21f, manifold);

            var pivot = new float3(-headRadius, top * 0.74f, side * headRadius);
            var lug = new float3(-headRadius * 2.6f, top, side * headRadius * 1.8f);
            var sleeveEnd = float3.Lerp(lug, pivot, 0.57f);
            mesh.Strut(lug, sleeveEnd, headRadius * 0.28f, shell);
            mesh.Strut(sleeveEnd, pivot, headRadius * 0.14f, manifold);

        }

        return mesh.Build(recipe.Output);

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

            var mesh = source.Meshes[instance.Mesh];

            for (var p = mesh.FirstPrimitive; p < mesh.FirstPrimitive + mesh.PrimitiveCount; p++) {

                var primitive = source.Primitives[p];

                for (var i = primitive.FirstIndex; i < primitive.FirstIndex + primitive.IndexCount; i++) {

                    var position = float3.Transform(source.Vertices[source.Indices[i]].Position, transform);

                    min = float3.Min(min, position);
                    max = float3.Max(max, position);

                }

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

        if (!recipe.CloseHeatShield) {

            return model;

        }

        var mesh = new MeshBuilder();
        var center = (model.BoundsMax + model.BoundsMin) * 0.5f;
        var radius = (model.BoundsMax.Z - model.BoundsMin.Z) * 0.5f;
        var baseY = model.BoundsMin.Y;
        var placement = float4x4.CreateTranslation(center.X, baseY, center.Z);
        var ablator = mesh.AddMaterial(Ablator(mesh, recipe.Seed, radius));
        var rim = mesh.AddMaterial(Material("shield_retainer", new float3(0.22f, 0.19f, 0.16f), 0.68f, 0.54f));
        var options = LatheOptions.Default with {

            VMetres = radius * 1.02f,

        };

        mesh.Lathe([new(0, -radius * 0.12f), new(radius * 0.5f, -radius * 0.098f), new(radius * 0.82f, -radius * 0.056f), new(radius * 0.99f, -radius * 0.018f)], ablator, options, placement);
        mesh.Lathe([new(radius * 0.99f, -radius * 0.018f), new(radius, 0), new(radius * 0.992f, radius * 0.016f)], rim, options, placement);
        mesh.Append(model, float4x4.Identity);

        return mesh.Build(recipe.Output);

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

            // The apron's top face uses site-local metres divided by width, with zero at the vehicle.
            var x = (u - MathF.Round(u)) * size;
            var z = (v - MathF.Round(v)) * size;
            var r = MathF.Sqrt(x * x + z * z);

            var grime = Noise.Fbm(u, v, 8, 6, recipe.Seed);
            var stains = Noise.Fbm(u, v, 3, 4, recipe.Seed + 11);
            var color = new float3(0.36f, 0.355f, 0.335f) * (0.82f + 0.3f * grime) * (0.85f + 0.2f * stains);
            var roughness = 0.88f - 0.1f * stains;

            var ju = MathF.Abs(u * joints % 1.0f - 0.5f) * 2;
            var jv = MathF.Abs(v * joints % 1.0f - 0.5f) * 2;
            var joint = MathF.Max(MathF.Exp(-MathF.Pow((1 - ju) * size / joints / 0.05f, 2)), MathF.Exp(-MathF.Pow((1 - jv) * size / joints / 0.05f, 2)));

            var pour = Noise.Hash((int)(u * joints), (int)(v * joints), joints, recipe.Seed);
            var aggregate = Noise.Value(u * 440, v * 440, 440, recipe.Seed);

            color *= (0.89f + 0.17f * pour) * (0.90f + 0.20f * aggregate) * (1 - 0.70f * joint);

            var scorch = MathF.Max(0, 1 - r / 26) * (0.7f + 0.3f * stains);

            color *= 1 - 0.75f * scorch * scorch;

            if (MathF.Abs(z) < 4.5f && x > 6 && x < half - 4) {

                color = new float3(0.035f) * (0.8f + 0.4f * grime);
                roughness = 0.95f;

            }

            if (MathF.Abs(r - 8) < 0.15f) {

                color = new float3(0.75f, 0.58f, 0.05f) * (1 - 0.6f * scorch);

            }

            var lane = MathF.Abs(z + 17);

            if (MathF.Abs(lane - 3.2f) < 0.16f || (lane < 0.14f && (x + half) % 8 < 3)) {

                color = new float3(0.64f, 0.60f, 0.48f) * (0.68f + 0.42f * aggregate);

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

        var galvanized = mesh.AddMaterial(Material("galvanized_steel", new float3(0.48f, 0.50f, 0.51f), 0.82f, 0.48f));
        var safety = mesh.AddMaterial(Material("safety_rail", new float3(0.78f, 0.44f, 0.045f), 0.05f, 0.50f));

        BuildTower(mesh, tower, 8, 72, steel);

        for (var floor = 12; floor <= 72; floor += 12) {

            ServiceDeck(mesh, tower + new float3(0, floor, 0), 5.1f, 4.8f, galvanized, safety);

        }

        for (var pipe = 0; pipe < 3; pipe++) {

            var offset = float4x4.CreateTranslation(tower + new float3(-3.1f + pipe * 0.85f, 0, 4.35f));
            var radius = 0.34f - pipe * 0.06f;

            mesh.Cylinder(radius, 0, 69 - pipe * 4, white, 16, offset);

            for (var y = 3; y < 69 - pipe * 4; y += 6) {

                mesh.Cylinder(radius + 0.055f, y, y + 0.12f, galvanized, 16, offset);

            }

        }

        mesh.Box(tower + new float3(-3.7f, 60, -3.8f), tower + new float3(2.4f, 63.1f, 2.5f), white);
        ServiceDeck(mesh, tower + new float3(7.8f, 48, 0), 3.2f, 1.25f, galvanized, safety);
        mesh.Strut(tower + new float3(4, 43, 1), tower + new float3(11, 48, 1), 0.32f, steel);
        mesh.Strut(tower + new float3(4, 43, -1), tower + new float3(11, 48, -1), 0.32f, steel);
        mesh.Cylinder(0.25f, 72, 92, mast, 12, float4x4.CreateTranslation(tower));

        BuildWaterTower(mesh, new float3(-38, 0, 34), white, steel);

        for (var tank = 0; tank < 3; tank++) {

            var x = 22 + tank * 8;
            var basePoint = new float3(x, 0, 41);
            var barrel = float4x4.CreateRotationX(MathF.PI / 2) * float4x4.CreateTranslation(basePoint + new float3(0, 3.15f, 0));
            ReadOnlySpan<float2> profile = [new(0, -8), new(1.45f, -7.7f), new(2.15f, -7.05f), new(2.35f, -6.2f), new(2.35f, 6.2f), new(2.15f, 7.05f), new(1.45f, 7.7f), new(0, 8)];

            mesh.Lathe(profile, white, LatheOptions.Default, barrel);

            for (var saddle = -4; saddle <= 4; saddle += 8) {

                mesh.Box(basePoint + new float3(-1.75f, 0, saddle - 0.40f), basePoint + new float3(1.75f, 2.1f, saddle + 0.40f), galvanized);

            }

            var connection = basePoint + new float3(0, 3.15f, -7.9f);
            var riser = basePoint + new float3(0, 3.15f, -10);

            mesh.Strut(connection, riser, 0.32f, galvanized);
            mesh.Strut(riser, riser - float3.UnitY * 2.4f, 0.32f, galvanized);

        }

        mesh.Strut(new float3(18, 0.75f, 31), new float3(40, 0.75f, 31), 0.42f, galvanized);

        BuildServiceLoop(mesh, half, recipe.Seed, galvanized, white);
        BuildExhaustChannel(mesh, half, galvanized);
        var lampFace = Material("floodlight_lens", new float3(0.8f, 0.78f, 0.71f), 0, 0.25f);
        lampFace.Emissive = new float3(6200, 5400, 4200);
        var lens = mesh.AddMaterial(lampFace);

        foreach (var lamp in LaunchComplex.Floodlights) {

            var direction = float3.Normalize(lamp.Aim - lamp.Position);
            var right = float3.Normalize(float3.Cross(float3.UnitY, direction));
            var up = float3.Cross(direction, right);
            var housing = float4x4.CreateFromQuaternion(FromTo(float3.UnitZ, direction)) * float4x4.CreateTranslation(lamp.Position);
            mesh.Box(new float3(-0.49f, -0.32f, -0.31f), new float3(0.49f, 0.32f, 0), galvanized, housing);
            mesh.Quad(lamp.Position + direction * 0.016f, right * 0.44f, up * 0.28f, lens);

        }

        var pole = LaunchComplex.Floodlights[^1].Position;
        mesh.Cylinder(0.22f, 0, pole.Y - 0.35f, galvanized, 16, float4x4.CreateTranslation(pole.X, 0, pole.Z));

        return mesh.Build(recipe.Output);

    }

    private static void BuildServiceLoop(MeshBuilder mesh, float apronHalf, int seed, int metal, int siding) {

        var asphalt = new TextureBuilder(256, 256);
        asphalt.Paint((u, v, t) => {

            var grain = Noise.Fbm(u, v, 70, 2, seed);
            var aggregate = Noise.Fbm(u, v, 4, 4, seed + 1);
            var lateral = MathF.Abs(u - 0.5f);
            var shoulder = Math.Clamp((lateral - 0.34f + 0.045f * (aggregate - 0.5f)) * 80, 0, 1);
            var track = MathF.Exp(-MathF.Pow((lateral - 0.15f) / 0.055f, 2));
            t.Color = float3.Lerp(new float3(0.066f, 0.072f, 0.073f), new float3(0.25f, 0.22f, 0.17f), shoulder);
            t.Color *= (0.61f + 0.53f * grain + 0.26f * aggregate) * (1 - 0.15f * track);

            if (MathF.Abs(lateral - 0.315f) < 0.007f || (lateral < 0.008f && v < 0.38f)) {

                t.Color = new float3(0.58f, 0.53f, 0.37f) * (0.6f + 0.4f * grain);

            }

        });

        var paving = Material("service_asphalt", new float3(1), 0, 0.88f);
        paving.BaseColorTexture = mesh.AddTexture(asphalt.BaseColor("service_asphalt"));
        var road = mesh.AddMaterial(paving);
        List<float3> path = [new(apronHalf, 0.09f, -17)];

        void Bend(float x, float z, float start) {

            for (var step = 0; step <= 12; step++) {

                var angle = start - step * MathF.PI / 24;
                path.Add(new float3(x + 17 * MathF.Cos(angle), 0.09f, z + 17 * MathF.Sin(angle)));

            }

        }

        Bend(89, -34, MathF.PI * 0.5f);
        Bend(89, -83, 0);
        Bend(-89, -83, -MathF.PI * 0.5f);
        Bend(-89, -34, -MathF.PI);
        path.Add(new float3(-apronHalf, 0.09f, -17));
        var previousSide = new float3(0, 0, 5);
        var length = 0f;

        for (var i = 1; i < path.Count; i++) {

            var tangent = float3.Normalize(path[Math.Min(i + 1, path.Count - 1)] - path[i - 1]);
            var side = float3.Cross(tangent, float3.UnitY) * 5;
            var next = length + (path[i] - path[i - 1]).Length();
            mesh.Quad(path[i - 1] - previousSide, path[i - 1] + previousSide, path[i] + side, path[i] - side, road, length / 9, next / 9);
            previousSide = side;
            length = next;

        }

        var foundation = mesh.AddMaterial(Material("utility_concrete", new float3(0.31f, 0.30f, 0.27f), 0, 0.88f));
        var doors = mesh.AddMaterial(Material("utility_trim", new float3(0.13f, 0.17f, 0.16f), 0.2f, 0.63f));
        mesh.Box(new float3(-54, -0.25f, -121), new float3(-11, 0.16f, -104.8f), foundation);
        mesh.Box(new float3(-50, 0.16f, -119), new float3(-17, 4.8f, -110), siding);
        mesh.Box(new float3(-50.4f, 4.8f, -119.4f), new float3(-16.6f, 5.05f, -109.6f), metal);

        for (var bay = 0; bay < 4; bay++) {

            var x = -46 + bay * 8;
            mesh.Box(new float3(x, 0.16f, -110), new float3(x + 3.7f, 3.6f, -109.86f), doors);

            for (var slat = 1; slat < 8; slat++) {

                var y = slat * 0.43f;
                mesh.Box(new float3(x, y, -109.86f), new float3(x + 3.7f, y + 0.035f, -109.82f), metal);

            }

        }

    }

    private static void BuildExhaustChannel(MeshBuilder mesh, float apronHalf, int metal) {

        var refractory = mesh.AddMaterial(Material("refractory_lining", new float3(0.13f, 0.115f, 0.097f), 0, 0.89f));

        for (var side = -1; side <= 1; side += 2) {

            var z = side * 4.7f;
            mesh.Box(new float3(7, 0, z - 0.32f), new float3(apronHalf - 4, 0.74f, z + 0.32f), refractory);

            for (var x = 8; x < apronHalf - 5; x += 5) {

                mesh.Box(new float3(x, 0, z - 0.65f), new float3(x + 0.45f, 0.87f, z + 0.65f), refractory);

            }

        }

        for (var bridge = 0; bridge < 2; bridge++) {

            var x = 16 + bridge * 25;
            for (var grate = 0; grate < 12; grate++) {

                var start = x + grate * 0.21f;
                mesh.Box(new float3(start, 0.73f, -4.8f), new float3(start + 0.11f, 0.88f, 4.8f), metal);

            }

        }

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

    private static void ServiceDeck(MeshBuilder mesh, float3 center, float halfWidth, float halfDepth, int floor, int rail) {

        mesh.Box(center + new float3(-halfWidth, -0.28f, -halfDepth), center + new float3(halfWidth, 0, halfDepth), floor);
        Span<float3> corners = [new(-halfWidth, 0, -halfDepth), new(halfWidth, 0, -halfDepth), new(halfWidth, 0, halfDepth), new(-halfWidth, 0, halfDepth)];

        for (var side = 0; side < corners.Length; side++) {

            var start = center + corners[side];
            var end = center + corners[(side + 1) % corners.Length];
            var posts = (int)MathF.Ceiling((end - start).Length() / 2);

            for (var post = 0; post < posts; post++) {

                var basePoint = start + (end - start) * ((float)post / posts);

                mesh.Strut(basePoint, basePoint + float3.UnitY * 1.1f, 0.07f, rail);

            }

            mesh.Strut(start + float3.UnitY * 0.55f, end + float3.UnitY * 0.55f, 0.06f, rail);
            mesh.Strut(start + float3.UnitY * 1.1f, end + float3.UnitY * 1.1f, 0.08f, rail);

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

        var shell = new float2[25];

        for (var ring = 0; ring < shell.Length; ring++) {

            var angle = MathF.PI * ring / (shell.Length - 1);
            shell[ring] = new float2(6 * MathF.Sin(angle), 3.4f - 4.9f * MathF.Cos(angle));

        }

        mesh.Lathe(shell, tank, options, bowl);
        mesh.Cylinder(0.67f, 0, top, tank, 24, float4x4.CreateTranslation(origin));

        for (var side = 0; side < feet.Length; side++) {

            var next = feet[(side + 1) % feet.Length];
            mesh.Strut(origin + feet[side] * 0.85f + float3.UnitY * 6, origin + next * 0.57f + float3.UnitY * 17, 0.16f, legs);

        }

    }

}
