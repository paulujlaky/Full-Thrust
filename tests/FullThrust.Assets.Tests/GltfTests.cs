using Brutal.Numerics;
using Brutal.TextureApi;
using FullThrust.Core.Parts;
using FullThrust.Core.Rendering;

namespace FullThrust.Assets.Tests;

public class GltfTests {

    private static string ModelsRoot => Path.Combine(AppContext.BaseDirectory, "data", "models");

    [Fact]
    public void GeneratedModelsSurviveAGlbRoundTrip() {

        var mesh = new MeshBuilder();
        var checker = new byte[4 * 4 * 4];

        for (var i = 0; i < 16; i++) {

            checker[i * 4] = (byte)((i + i / 4) % 2 * 255);
            checker[i * 4 + 3] = 255;

        }

        var material = new MaterialData { Name = "painted", BaseColor = new float4(0.5f, 0.6f, 0.7f, 1), Metallic = 0.25f, Roughness = 0.75f, Emissive = new float3(0.1f, 0, 0) };

        material.BaseColorTexture = mesh.AddTexture(new TextureData { Name = "checker", Width = 4, Height = 4, Pixels = checker, Srgb = true });

        var paint = mesh.AddMaterial(material);
        var metal = mesh.AddMaterial(new MaterialData { Name = "metal", Metallic = 1, Roughness = 0.3f });

        mesh.Cylinder(1, -2, 2, paint, 32);
        mesh.Box(new float3(-0.1f, -1, -1.2f), new float3(0.1f, 1, -0.9f), metal);

        var model = mesh.Build("roundtrip");
        var path = Path.Combine(Path.GetTempPath(), $"fullthrust-{Guid.NewGuid():N}.glb");

        try {

            GltfModelWriter.WriteGlb(model, path);

            var loaded = GltfModelLoader.Load(path);

            Assert.Equal(model.TriangleCount, loaded.TriangleCount);
            Assert.Equal(model.BoundsMin, loaded.BoundsMin);
            Assert.Equal(model.BoundsMax, loaded.BoundsMax);
            Assert.Equal(2, loaded.Materials.Length);

            var painted = loaded.Materials[0];

            Assert.Equal(material.BaseColor, painted.BaseColor);
            Assert.Equal(0.25f, painted.Metallic);
            Assert.Equal(0.75f, painted.Roughness);
            Assert.Equal(material.Emissive, painted.Emissive);

            var texture = loaded.Textures[painted.BaseColorTexture];

            Assert.True(texture.Srgb);
            Assert.Equal(checker, texture.Pixels);

        } finally {

            File.Delete(path);

        }

    }

    [Fact]
    public void PngEncoderOutputDecodes() {

        var pixels = new byte[7 * 5 * 4];

        for (var i = 0; i < pixels.Length; i++) {

            pixels[i] = (byte)(i * 37 % 256);

        }

        var texture = TextureLoader.LoadFromMemory(PngEncoder.Encode(pixels, 7, 5), TextureLoader.FormatType.Png);

        try {

            Assert.Equal(7, texture.Extent.X);
            Assert.Equal(5, texture.Extent.Y);

            var data = texture.ImageData(0, 0, 0);
            var pitch = (int)texture.RowPitch(0).Bytes;

            for (var y = 0; y < 5; y++) {

                Assert.True(data.Slice(y * pitch, 28).SequenceEqual(pixels.AsSpan(y * 28, 28)));

            }

        } finally {

            TextureLoader.Unload(texture);

        }

    }

    [Fact]
    public void NasaEngineLoadsWithItsNodeTransform() {

        var model = GltfModelLoader.Load(Path.Combine(ModelsRoot, "nasa", "ssme.glb"));

        Assert.Equal(5686, model.TriangleCount);

        // The 3DS root node turns Z-up into Y-up; the bell axis stays along X.
        Assert.True(model.BoundsMax.X - model.BoundsMin.X > 100);
        Assert.Equal(model.BoundsMax.Y - model.BoundsMin.Y, model.BoundsMax.Z - model.BoundsMin.Z, 5);

    }

    [Fact]
    public void GeminiTexturesUseThePngFallbacks() {

        var model = GltfModelLoader.Load(Path.Combine(ModelsRoot, "parts", "pod-pinnacle.glb"));

        Assert.NotEmpty(model.Textures);
        Assert.All(model.Textures, texture => Assert.Equal(texture.Width * texture.Height * 4, texture.Pixels.Length));

    }

    // Generated part models are authored in part space, so they must match the part's size without fitting.
    [Fact]
    public void GeneratedPartModelsMatchTheirParts() {

        var models = new ModelCatalog();
        var catalog = PartCatalogLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "parts", "parts.json"), models);

        foreach (var part in catalog.Parts) {

            Assert.NotNull(part.Model);

            Assert.True(File.Exists(Path.Combine(ModelsRoot, part.Model!.File)), $"{part.Id}: missing {part.Model.File}");

            if (part.Model.Fit != ModelFit.None) {

                continue;

            }

            var model = GltfModelLoader.Load(Path.Combine(ModelsRoot, part.Model.File));
            var size = model.BoundsMax - model.BoundsMin;

            Assert.InRange(size.Y, part.Length * 0.92, part.Length * 1.02);
            // Radial parts such as RCS blocks are shaped by their mounting, not a stack diameter.
            if (part.Category != "rcs") {

                Assert.InRange(size.X, part.Radius * 2 * 0.9, part.Radius * 2 * 1.15);

            }

        }

    }

}
