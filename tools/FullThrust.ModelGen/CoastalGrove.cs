using Brutal.Numerics;
using FullThrust.Assets;

namespace FullThrust.ModelGen;

public static class CoastalGrove {

    public static ModelData Build(Recipe recipe) {

        var mesh = new MeshBuilder();
        var random = new Random(recipe.Seed);
        var pine = recipe.Pattern == "pine";
        var foliage = mesh.AddMaterial(new MaterialData {

            Name = "leaf_sprays",
            BaseColorTexture = mesh.AddTexture(LeafSpray(recipe.Seed, pine)),
            Metallic = 0,
            Roughness = 0.92f,
            AlphaMode = AlphaMode.Mask,
            AlphaCutoff = 0.37f,
            DoubleSided = true,

        });
        var bark = mesh.AddMaterial(new MaterialData {

            Name = "weathered_bark",
            BaseColor = new float4(0.115f, 0.083f, 0.055f, 1),
            Metallic = 0,
            Roughness = 0.93f,

        });

        for (var tree = 0; tree < (pine ? 11 : 18); tree++) {

            var angle = MathF.Tau * random.NextSingle();
            var radius = MathF.Sqrt(random.NextSingle()) * (float)recipe.Diameter * 0.45f;
            var foot = new float3(MathF.Cos(angle) * radius, -0.5f, MathF.Sin(angle) * radius);
            var height = (float)recipe.Length * (0.44f + 0.56f * random.NextSingle());
            var lean = new float3(random.NextSingle() - 0.35f, 0, random.NextSingle() - 0.5f) * height * 0.12f;
            var axis = float3.UnitY * height + lean;
            mesh.Strut(foot, foot + axis * 0.63f, height * 0.027f, bark);
            mesh.Strut(foot + axis * 0.63f, foot + axis * 0.94f, height * 0.015f, bark);
            var limbs = pine ? 25 : 19;

            for (var limb = 0; limb < limbs; limb++) {

                var t = (limb + random.NextSingle()) / limbs;
                var branchHeight = pine ? 0.49f + t * 0.50f : 0.21f + t * 0.76f;
                var heading = tree + limb * 2.39996f + 0.56f * random.NextSingle();
                var along = new float3(MathF.Cos(heading), 0, MathF.Sin(heading));
                var side = float3.Cross(float3.UnitY, along);
                var width = height * (pine ? 0.19f : 0.39f) * (0.65f + 0.55f * random.NextSingle()) * MathF.Sqrt(1.05f - t);
                var root = foot + axis * branchHeight;
                var tip = root + along * width + float3.UnitY * width * 0.26f;
                mesh.Strut(root, tip, height * 0.012f, bark);
                var spread = width * (pine ? 0.92f : 1.12f);

                mesh.Quad(tip, side * spread, along * spread * 0.54f + float3.UnitY * spread * 0.64f, foliage);
                mesh.Quad(tip + float3.UnitY * spread * 0.16f, (along + side * 0.2f) * spread, (float3.UnitY - side * 0.53f) * spread * 0.68f, foliage);

            }

        }

        return mesh.Build(recipe.Output);

    }

    private static TextureData LeafSpray(int seed, bool pine) {

        const int size = 128;
        var pixels = new byte[size * size * 4];

        for (var y = 0; y < size; y++) {

            for (var x = 0; x < size; x++) {

                var u = (x + 0.5f) / size;
                var v = (y + 0.5f) / size;
                var leaf = Noise.Fbm(u, v, 8, 4, seed);
                var gaps = Noise.Fbm(u, v, 18, 3, seed + 7);
                var px = 2 * u - 1;
                var py = 2 * v - 1;
                var outline = 1 - MathF.Sqrt(px * px + py * py);
                var opacity = Math.Clamp((outline + (leaf - 0.60f) * 1.65f) * 9, 0, 1);
                opacity *= Math.Clamp((gaps - 0.24f) * 6, 0, 1);
                var light = 0.60f + 0.63f * leaf + 0.18f * (1 - v);
                var offset = (y * size + x) * 4;
                pixels[offset] = (byte)((pine ? 63 : 83) * light);
                pixels[offset + 1] = (byte)((pine ? 79 : 95) * light);
                pixels[offset + 2] = (byte)((pine ? 53 : 55) * light);
                pixels[offset + 3] = (byte)(255 * opacity);

            }

        }

        return new TextureData {

            Name = "coastal_leaf_spray",
            Srgb = true,
            Pixels = pixels,
            Width = size,
            Height = size,

        };

    }

}
