using Brutal.Numerics;
using FullThrust.Assets;

namespace FullThrust.ModelGen;

// Tileable procedural maps: a height field drives the normal map, and colour/roughness/metal are painted per texel.
public sealed class TextureBuilder {

    private readonly float[] _height;
    private readonly float3[] _color;
    private readonly float[] _roughness;
    private readonly float[] _metallic;
    private readonly float[] _occlusion;

    public TextureBuilder(int width, int height) {

        Width = width;
        Height = height;

        var count = width * height;

        _height = new float[count];
        _color = new float3[count];
        _roughness = new float[count];
        _metallic = new float[count];
        _occlusion = new float[count];

        Array.Fill(_occlusion, 1);

    }

    public int Width { get; }

    public int Height { get; }

    // u, v in [0, 1) at texel centres.
    public void Paint(Action<float, float, Texel> painter) {

        var texel = new Texel();

        for (var y = 0; y < Height; y++) {

            for (var x = 0; x < Width; x++) {

                var i = y * Width + x;

                texel.Color = _color[i];
                texel.Roughness = _roughness[i];
                texel.Metallic = _metallic[i];
                texel.Height = _height[i];
                texel.Occlusion = _occlusion[i];

                painter((x + 0.5f) / Width, (y + 0.5f) / Height, texel);

                _color[i] = texel.Color;
                _roughness[i] = texel.Roughness;
                _metallic[i] = texel.Metallic;
                _height[i] = texel.Height;
                _occlusion[i] = texel.Occlusion;

            }

        }

    }

    public TextureData BaseColor(string name) => Encode(name, true, i => new float4(LinearToSrgb(_color[i]), 1));

    // glTF packing: R occlusion, G roughness, B metallic.
    public TextureData MetallicRoughness(string name) => Encode(name, false, i => new float4(_occlusion[i], _roughness[i], _metallic[i], 1));

    // Height is in metres of relief; texelMetres converts texel spacing to the same units.
    public TextureData Normal(string name, float texelMetresU, float texelMetresV) {

        return Encode(name, false, i => {

            var x = i % Width;
            var y = i / Width;

            var left = _height[y * Width + (x + Width - 1) % Width];
            var right = _height[y * Width + (x + 1) % Width];
            var up = _height[(y + Height - 1) % Height * Width + x];
            var down = _height[(y + 1) % Height * Width + x];

            // Image rows follow +V, which is the tangent-space bitangent.
            var dhdu = (right - left) / (2 * texelMetresU);
            var dhdv = (down - up) / (2 * texelMetresV);
            var n = float3.Normalize(new float3(-dhdu, -dhdv, 1));

            return new float4(n * 0.5f + new float3(0.5f), 1);

        });

    }

    private TextureData Encode(string name, bool srgb, Func<int, float4> texel) {

        var pixels = new byte[Width * Height * 4];

        for (var i = 0; i < Width * Height; i++) {

            var value = float4.Clamp(texel(i), float4.Zero, new float4(1));

            pixels[i * 4] = (byte)MathF.Round(value.X * 255);
            pixels[i * 4 + 1] = (byte)MathF.Round(value.Y * 255);
            pixels[i * 4 + 2] = (byte)MathF.Round(value.Z * 255);
            pixels[i * 4 + 3] = (byte)MathF.Round(value.W * 255);

        }

        return new TextureData { Name = name, Width = Width, Height = Height, Pixels = pixels, Srgb = srgb };

    }

    private static float3 LinearToSrgb(float3 c) => new(ToSrgb(c.X), ToSrgb(c.Y), ToSrgb(c.Z));

    private static float ToSrgb(float c) {

        c = Math.Clamp(c, 0, 1);

        return c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f;

    }

    public sealed class Texel {

        public float3 Color;

        public float Roughness;
        public float Metallic;
        public float Occlusion;

        public float Height;

    }

}

public static class Noise {

    // Periodic value noise in [0, 1]: tiles every `period` cells in both axes.
    public static float Value(float x, float y, int period, int seed) {

        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var fx = x - x0;
        var fy = y - y0;

        var sx = fx * fx * (3 - 2 * fx);
        var sy = fy * fy * (3 - 2 * fy);

        var a = Hash(x0, y0, period, seed);
        var b = Hash(x0 + 1, y0, period, seed);
        var c = Hash(x0, y0 + 1, period, seed);
        var d = Hash(x0 + 1, y0 + 1, period, seed);

        return float.Lerp(float.Lerp(a, b, sx), float.Lerp(c, d, sx), sy);

    }

    // Fractal sum over octaves for u, v in [0, 1); stays tileable.
    public static float Fbm(float u, float v, int baseCells, int octaves, int seed) {

        var sum = 0f;
        var amplitude = 0.5f;
        var total = 0f;
        var cells = baseCells;

        for (var o = 0; o < octaves; o++) {

            sum += amplitude * Value(u * cells, v * cells, cells, seed + o * 101);
            total += amplitude;
            amplitude *= 0.5f;
            cells *= 2;

        }

        return sum / total;

    }

    public static float Hash(int x, int y, int period, int seed) {

        x = ((x % period) + period) % period;
        y = ((y % period) + period) % period;

        var h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);

        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;

        return (h & 0xFFFFFF) / (float)0xFFFFFF;

    }

}
