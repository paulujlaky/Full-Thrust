using Brutal.Numerics;

namespace FullThrust.Core.Rendering;

public readonly record struct Floodlight(float3 Position, float3 Aim, float Candela);

public static class LaunchComplex {

    // The generator and renderer must put the luminaires at the same pad-local coordinates.
    public static ReadOnlySpan<Floodlight> Floodlights => Fixtures;

    private static readonly Floodlight[] Fixtures = [

        new(new float3(-16.8f, 25, 3.6f), new float3(0, 8, 0), 90000),
        new(new float3(-16.8f, 61, -3.6f), new float3(0, 43, 0), 160000),
        new(new float3(27, 15, -35), new float3(0, 24, 0), 235000),

    ];

}
