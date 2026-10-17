using System.Runtime.InteropServices;
using Brutal.Numerics;

namespace FullThrust.Core.Terrain;

// Chunk-local vertex: position relative to the chunk centre in the body-fixed frame, packed sRGB albedo with roughness in alpha.
[StructLayout(LayoutKind.Sequential)]
public struct TerrainVertex {

    public float3 Position;
    public float3 Normal;
    public uint Color;

    // Signed metres above sea level before clamping, so the shader can draw the shoreline between vertices.
    public float Height;

}

// One quadtree chunk: a grid plus skirts hiding cracks between levels. One builder per thread; Build does not allocate.
public sealed class TerrainChunkBuilder {

    public const int Quads = 32;
    public const int Side = Quads + 1;
    public const int GridVertices = Side * Side;
    public const int VertexCount = GridVertices + 4 * Side;

    private const int Border = Side + 2;

    public static readonly ushort[] Indices = BuildIndices();

    private readonly double3[] _positions = new double3[Border * Border];
    private readonly double3[] _directions = new double3[Border * Border];
    private readonly TerrainSample[] _samples = new TerrainSample[Border * Border];

    public TerrainBounds Build(ITerrain terrain, TerrainPalette palette, double radius, in TerrainNode node, Span<TerrainVertex> output) {

        var step = node.Size / Quads;
        var edge = CubeSphere.EdgeLength(node, radius);
        var resolution = edge / Quads;
        var ocean = terrain.HasOcean;
        var mean = 0.0;

        for (var gy = 0; gy < Border; gy++) {

            for (var gx = 0; gx < Border; gx++) {

                var i = gy * Border + gx;
                var direction = CubeSphere.Direction(node.Face, node.U0 + (gx - 1) * step, node.V0 + (gy - 1) * step);
                var sample = terrain.Sample(direction, resolution);
                var height = ocean ? Math.Max(sample.Height, 0) : sample.Height;

                _directions[i] = direction;
                _samples[i] = sample;
                _positions[i] = direction * (radius + height);

                mean += height;

            }

        }

        var center = CubeSphere.Center(node) * (radius + mean / (Border * Border));
        var skirt = edge * 0.02 + 2;
        var extent = 0.0;

        for (var y = 0; y < Side; y++) {

            for (var x = 0; x < Side; x++) {

                var i = (y + 1) * Border + x + 1;
                var direction = _directions[i];
                var sample = _samples[i];

                var tangent = _positions[i + 1] - _positions[i - 1];
                var bitangent = _positions[i + Border] - _positions[i - Border];
                var normal = double3.Normalize(double3.Cross(tangent, bitangent));

                if (double3.Dot(normal, direction) < 0) {

                    normal = -normal;

                }

                var slope = (float)(1 - double3.Dot(normal, direction));
                var surface = palette.Surface(sample, direction, slope);
                var local = _positions[i] - center;

                output[y * Side + x] = new TerrainVertex { Position = ToFloat(local), Normal = ToFloat(normal), Color = Pack(surface), Height = (float)sample.Height };

                extent = Math.Max(extent, local.Length());

            }

        }

        for (var k = 0; k < Side; k++) {

            Skirt(output, 0, k, EdgeIndex(0, k), center, skirt);
            Skirt(output, 1, k, EdgeIndex(1, k), center, skirt);
            Skirt(output, 2, k, EdgeIndex(2, k), center, skirt);
            Skirt(output, 3, k, EdgeIndex(3, k), center, skirt);

        }

        return new TerrainBounds(center, extent + skirt);

    }

    private void Skirt(Span<TerrainVertex> output, int edge, int k, int gridIndex, double3 center, double depth) {

        var x = gridIndex % Side;
        var y = gridIndex / Side;
        var direction = _directions[(y + 1) * Border + x + 1];
        var vertex = output[gridIndex];

        vertex.Position = ToFloat(_positions[(y + 1) * Border + x + 1] - direction * depth - center);

        output[GridVertices + edge * Side + k] = vertex;

    }

    // Edges: 0 bottom (y = 0), 1 top (y = Quads), 2 left (x = 0), 3 right (x = Quads).
    private static int EdgeIndex(int edge, int k) => edge switch {

        0 => k,
        1 => Quads * Side + k,
        2 => k * Side,
        _ => k * Side + Quads,

    };

    private static ushort[] BuildIndices() {

        var indices = new List<ushort>(Quads * Quads * 6 + 4 * Quads * 6);

        for (var y = 0; y < Quads; y++) {

            for (var x = 0; x < Quads; x++) {

                var a = y * Side + x;
                var b = a + 1;
                var c = a + Side;
                var d = c + 1;

                indices.AddRange([(ushort)a, (ushort)b, (ushort)c, (ushort)b, (ushort)d, (ushort)c]);

            }

        }

        for (var edge = 0; edge < 4; edge++) {

            for (var k = 0; k < Quads; k++) {

                var e0 = EdgeIndex(edge, k);
                var e1 = EdgeIndex(edge, k + 1);
                var s0 = GridVertices + edge * Side + k;
                var s1 = s0 + 1;

                indices.AddRange([(ushort)e0, (ushort)s0, (ushort)e1, (ushort)e1, (ushort)s0, (ushort)s1]);

            }

        }

        return indices.ToArray();

    }

    private static float3 ToFloat(double3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    public static uint Pack(float4 surface) {

        var r = (uint)MathF.Round(ToSrgb(surface.X) * 255);
        var g = (uint)MathF.Round(ToSrgb(surface.Y) * 255);
        var b = (uint)MathF.Round(ToSrgb(surface.Z) * 255);
        var a = (uint)MathF.Round(Math.Clamp(surface.W, 0, 1) * 255);

        return r | (g << 8) | (b << 16) | (a << 24);

    }

    private static float ToSrgb(float c) {

        c = Math.Clamp(c, 0, 1);

        return c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f;

    }

}
