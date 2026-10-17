using Brutal.Numerics;

namespace FullThrust.Core.Terrain;

// Surface albedo and roughness from terrain samples, driven by the colours in the body's terrain data.
public sealed class TerrainPalette {

    private readonly bool _cratered;

    public float3 DeepWater { get; }

    public float3 ShallowWater { get; }

    private readonly float3 _beach;
    private readonly float3 _lowland;
    private readonly float3 _dry;
    private readonly float3 _highland;
    private readonly float3 _rock;
    private readonly float3 _snow;
    private readonly float3 _mare;
    private readonly float3 _ejecta;

    public TerrainPalette(TerrainData data) {

        _cratered = string.Equals(data.Type, "cratered", StringComparison.OrdinalIgnoreCase);

        DeepWater = Color(data, "deepWater", new float3(0.006f, 0.02f, 0.045f));
        ShallowWater = Color(data, "shallowWater", new float3(0.02f, 0.07f, 0.09f));

        _beach = Color(data, "beach", new float3(0.55f, 0.5f, 0.38f));
        _lowland = Color(data, "lowland", new float3(0.07f, 0.12f, 0.035f));
        _dry = Color(data, "dry", new float3(0.32f, 0.26f, 0.15f));
        _highland = Color(data, "highland", new float3(0.2f, 0.17f, 0.12f));
        _rock = Color(data, "rock", new float3(0.22f, 0.2f, 0.18f));
        _snow = Color(data, "snow", new float3(0.85f, 0.87f, 0.9f));
        _mare = Color(data, "mare", new float3(0.1f));
        _ejecta = Color(data, "ejecta", new float3(0.48f, 0.47f, 0.45f));

    }

    // Ground (or sea bed) albedo and roughness; the shader draws water over it. Slope is 1 - cos(tilt).
    public float4 Surface(in TerrainSample sample, double3 direction, float slope) {

        if (_cratered) {

            var albedo = float3.Lerp(_highland, _mare, sample.Moisture);

            albedo = float3.Lerp(albedo, _ejecta, sample.Feature * 0.55f);
            albedo = float3.Lerp(albedo, _rock, Step(0.12f, 0.35f, slope) * 0.5f);

            return new float4(albedo, 0.95f);

        }

        var height = (float)sample.Height;

        var latitude = MathF.Abs((float)Math.Asin(Math.Clamp(direction.Y, -1, 1))) * (180 / MathF.PI);

        // Subtropical deserts and wetter mid-latitudes, broken up by the moisture field.
        var arid = Step(0.62f, 0.25f, sample.Moisture) * (1 - Step(18, 38, MathF.Abs(latitude - 26)) * 0.7f);
        var land = float3.Lerp(_lowland, _dry, Math.Clamp(arid + Step(1200, 2600, height) * 0.4f, 0, 1));

        land = float3.Lerp(land, _highland, Step(900, 2800, height));
        land = float3.Lerp(land, _beach, Step(5, 1, height));
        land = float3.Lerp(land, _rock, Math.Clamp(Step(0.18f, 0.4f, slope) + sample.Feature * 0.35f, 0, 1));

        var snowline = 5200 - 5000 * Step(30, 75, latitude);
        var snow = Step(snowline - 300, snowline + 300, height) * (1 - Step(0.45f, 0.7f, slope));

        land = float3.Lerp(land, _snow, snow);

        return new float4(land, 0.92f - 0.4f * snow);

    }

    private static float Step(float edge0, float edge1, float x) {

        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);

        return t * t * (3 - 2 * t);

    }

    private static float3 Color(TerrainData data, string name, float3 fallback) {

        return data.Colors.TryGetValue(name, out var c) && c.Length >= 3 ? new float3((float)c[0], (float)c[1], (float)c[2]) : fallback;

    }

}
