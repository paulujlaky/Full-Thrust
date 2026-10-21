using System.Runtime.CompilerServices;

namespace FullThrust.Core.Terrain;

// Surface classes the palette blends between; each body maps them to detail materials in its terrain data.
public enum TerrainChannel {

    Lowland,
    Dry,
    Highland,
    Beach,
    Rock,
    Snow,
    Mare,
    Ejecta,

}

[InlineArray(Count)]
public struct TerrainWeights {

    public const int Count = 8;

    private float _element;

    public static TerrainWeights Only(TerrainChannel channel) {

        var weights = new TerrainWeights();

        weights[(int)channel] = 1;

        return weights;

    }

    // Mirrors float3.Lerp(current, colour of channel, t), so the weights always describe the blended colour.
    public void Lerp(TerrainChannel channel, float t) {

        for (var i = 0; i < Count; i++) {

            this[i] *= 1 - t;

        }

        this[(int)channel] += t;

    }

    // Four channels per word as unorm bytes, lowest channel in the low byte.
    public readonly uint Pack(int first) {

        var packed = 0u;

        for (var i = 0; i < 4; i++) {

            packed |= (uint)MathF.Round(Math.Clamp(this[first + i], 0, 1) * 255) << (i * 8);

        }

        return packed;

    }

}
