using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.StbApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using FullThrust.Core.Terrain;
using FullThrust.Game.Rendering;

namespace FullThrust.Game.Terrain;

[InlineArray(TerrainWeights.Count)]
public struct ChannelVectors {

    private float4 _element;

}

// Mirrors TerrainBody in Shaders/terrain_materials.glsl (std140).
[StructLayout(LayoutKind.Sequential)]
public struct TerrainBodyUniforms {

    // w: 1 on bodies with an ocean; ShallowWater.w: city light luminance, 0 for none.
    public float4 DeepWater;
    public float4 ShallowWater;

    // Texture array layer for each channel, -1 where the channel has no detail material.
    public int4 Layers0;
    public int4 Layers1;

    // Repeats per metre for each channel.
    public float4 TileScale0;
    public float4 TileScale1;

    // rgb: palette colour; w: 1 / mean luminance of the channel's texture.
    public ChannelVectors Palette;

    // rgb: 1 / mean colour of the channel's texture, so detail averages back to the palette at a distance; w: mean height.
    public ChannelVectors InverseMean;

}

[InlineArray(TerrainMaterials.MaxBodies)]
public struct TerrainBodyArray {

    private TerrainBodyUniforms _element;

}

// Per-body detail texture arrays and uniforms mapping palette channels to layers.
public sealed class TerrainMaterials : IDisposable {

    public const int MaxBodies = 4;

    private readonly DeviceEx _device;

    private readonly GpuTexture? _albedo;
    private readonly GpuTexture? _surface;
    private readonly GpuTexture _fallback;
    private readonly BufferEx _uniforms;
    private readonly VkSampler _sampler;
    private readonly VkDescriptorPool _pool;

    public TerrainMaterials(DeviceEx device, GpuUploader uploader, float anisotropy, string texturesRoot, IReadOnlyList<(TerrainData Data, TerrainPalette Palette)> bodies) {

        _device = device;

        if (bodies.Count > MaxBodies) {

            throw new InvalidOperationException($"At most {MaxBodies} bodies can have terrain materials.");

        }

        var layers = new List<string>();

        foreach (var (data, _) in bodies) {

            foreach (var material in data.Materials) {

                if (!layers.Contains(material.Texture)) {

                    layers.Add(material.Texture);

                }

            }

        }

        var means = new float4[layers.Count];

        if (layers.Count > 0) {

            var (albedo, surface, size) = LoadLayers(texturesRoot, layers, means);

            _albedo = new GpuTexture(uploader, "TerrainAlbedo", albedo, size, size, srgb: true, layers: layers.Count);
            _surface = new GpuTexture(uploader, "TerrainSurface", surface, size, size, srgb: false, layers: layers.Count);

        }

        _fallback = new GpuTexture(uploader, "TerrainFallback", [128, 128, 255, 255], 1, 1, srgb: false, layers: 1);

        var uniforms = new TerrainBodyArray();

        for (var b = 0; b < bodies.Count; b++) {

            uniforms[b] = Describe(bodies[b].Data, bodies[b].Palette, layers, means);

        }

        _uniforms = uploader.CreateDeviceBuffer<TerrainBodyArray>("TerrainBodies", [uniforms], VkBufferUsageFlags.UniformBufferBit);
        uploader.Flush();

        _sampler = device.CreateSampler(new VkSamplerCreateInfo {

            MagFilter = VkFilter.Linear,
            MinFilter = VkFilter.Linear,
            MipmapMode = VkSamplerMipmapMode.Linear,
            AddressModeU = VkSamplerAddressMode.Repeat,
            AddressModeV = VkSamplerAddressMode.Repeat,
            AddressModeW = VkSamplerAddressMode.Repeat,
            AnisotropyEnable = anisotropy > 1,
            // Every pixel takes up to a dozen triplanar fetches; 16x anisotropy cost a third of the frame for little gain.
            MaxAnisotropy = Math.Min(anisotropy, 4.0f),
            MaxLod = 32,

        }, null);

        SetLayout = Descriptors.CreateLayout(device, [

            Descriptors.Binding(0, VkDescriptorType.UniformBuffer, VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit),
            Descriptors.Binding(1, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.FragmentBit),
            Descriptors.Binding(2, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.FragmentBit),

        ]);

        _pool = Descriptors.CreatePool(device, 1, [

            new VkDescriptorPoolSize { Type = VkDescriptorType.UniformBuffer, DescriptorCount = 1 },
            new VkDescriptorPoolSize { Type = VkDescriptorType.CombinedImageSampler, DescriptorCount = 2 },

        ]);

        Set = Descriptors.Allocate(device, _pool, SetLayout);

        Descriptors.WriteUniformBuffer(device, Set, 0, _uniforms.VkBuffer, ByteSize.Of<TerrainBodyArray>());
        Descriptors.WriteImage(device, Set, 1, (_albedo ?? _fallback).View, _sampler);
        Descriptors.WriteImage(device, Set, 2, (_surface ?? _fallback).View, _sampler);

    }

    public VkDescriptorSetLayout SetLayout { get; }

    public VkDescriptorSet Set { get; }

    private static TerrainBodyUniforms Describe(TerrainData data, TerrainPalette palette, List<string> layers, float4[] means) {

        var result = new TerrainBodyUniforms {

            DeepWater = new float4(palette.DeepWater, data.Ocean ? 1 : 0),
            ShallowWater = new float4(palette.ShallowWater, (float)data.CityLights),

        };

        Span<int> layerOf = stackalloc int[TerrainWeights.Count];
        Span<float> scaleOf = stackalloc float[TerrainWeights.Count];

        layerOf.Fill(-1);
        scaleOf.Fill(1);

        foreach (var material in data.Materials) {

            if (!Enum.TryParse<TerrainChannel>(material.Channel, ignoreCase: true, out var channel)) {

                throw new InvalidDataException($"Unknown terrain channel '{material.Channel}'.");

            }

            var c = (int)channel;
            var layer = layers.IndexOf(material.Texture);
            var scale = (float)(1 / Math.Max(material.TileSize, 0.01));
            var color = palette.ChannelColor(channel);
            var mean = means[layer];

            layerOf[c] = layer;
            scaleOf[c] = scale;

            var luminance = 0.2126f * mean.X + 0.7152f * mean.Y + 0.0722f * mean.Z;

            result.Palette[c] = new float4(color, 1 / Math.Max(luminance, 1e-3f));
            result.InverseMean[c] = new float4(1 / Math.Max(mean.X, 1e-3f), 1 / Math.Max(mean.Y, 1e-3f), 1 / Math.Max(mean.Z, 1e-3f), mean.W);

        }

        result.Layers0 = new int4(layerOf[0], layerOf[1], layerOf[2], layerOf[3]);
        result.Layers1 = new int4(layerOf[4], layerOf[5], layerOf[6], layerOf[7]);
        result.TileScale0 = new float4(scaleOf[0], scaleOf[1], scaleOf[2], scaleOf[3]);
        result.TileScale1 = new float4(scaleOf[4], scaleOf[5], scaleOf[6], scaleOf[7]);

        return result;

    }

    private static (byte[] Albedo, byte[] Surface, int Size) LoadLayers(string root, List<string> layers, float4[] means) {

        byte[]? albedo = null;
        byte[]? surface = null;
        var size = 0;

        for (var layer = 0; layer < layers.Count; layer++) {

            var id = layers[layer];
            var folder = Path.Combine(root, id);

            using var diffuse = Load(folder, id, "diff");
            using var height = Load(folder, id, "disp");
            using var normal = Load(folder, id, "nor_gl");
            using var arm = Load(folder, id, "arm");

            if (layer == 0) {

                size = diffuse.Width;
                albedo = new byte[size * size * 4 * layers.Count];
                surface = new byte[size * size * 4 * layers.Count];

            }

            foreach (var image in (ReadOnlySpan<int>)[diffuse.Width, diffuse.Height, height.Width, height.Height, normal.Width, normal.Height, arm.Width, arm.Height]) {

                if (image != size) {

                    throw new InvalidDataException($"Terrain texture set '{id}' must be {size}x{size} like the first set.");

                }

            }

            var d = diffuse.AsSpan();
            var h = height.AsSpan();
            var n = normal.AsSpan();
            var a = arm.AsSpan();

            var offset = layer * size * size * 4;
            var sum = double3.Zero;
            var heights = 0.0;

            for (var i = 0; i < size * size; i++) {

                var o = offset + i * 4;

                albedo![o] = d[i * 4];
                albedo[o + 1] = d[i * 4 + 1];
                albedo[o + 2] = d[i * 4 + 2];
                albedo[o + 3] = h[i * 4];

                surface![o] = n[i * 4];
                surface[o + 1] = n[i * 4 + 1];
                surface[o + 2] = a[i * 4 + 1];
                surface[o + 3] = a[i * 4];

                sum += new double3(SrgbToLinear(d[i * 4]), SrgbToLinear(d[i * 4 + 1]), SrgbToLinear(d[i * 4 + 2]));
                heights += h[i * 4] / 255.0;

            }

            var mean = sum / (size * size);

            means[layer] = new float4((float)mean.X, (float)mean.Y, (float)mean.Z, (float)(heights / (size * size)));

        }

        return (albedo!, surface!, size);

    }

    private static StbImage8 Load(string folder, string id, string map) {

        var path = Path.Combine(folder, $"{id}_{map}_1k.jpg");

        if (!File.Exists(path)) {

            throw new FileNotFoundException($"Missing terrain texture '{path}'.");

        }

        return Stb.Load(path, 4);

    }

    private static double SrgbToLinear(byte value) {

        var c = value / 255.0;

        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    }

    public void Dispose() {

        _device.DestroyDescriptorPool(_pool, null);
        _device.DestroyDescriptorSetLayout(SetLayout, null);
        _device.DestroySampler(_sampler, null);

        _uniforms.Dispose();
        _albedo?.Dispose();
        _surface?.Dispose();
        _fallback.Dispose();

    }

}
