using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Space;

namespace FullThrust.Game.Rendering;

public enum Tonemapper {

    Aces,
    AgX,
    PbrNeutral,
    Reinhard,
    Clamp,

}

// Physical light levels for the frame: lux for the sun, with a hemisphere ambient standing in for sky and planetshine.
public sealed class SceneLighting {

    // Rough clear-sky irradiance on a horizontal surface with the sun high, as a fraction of direct sun.
    private const double SkyFraction = 0.18;

    private const double PlanetAlbedo = 0.3;

    private static readonly double3 SkyTint = new(0.45, 0.62, 1.0);

    private static readonly double3 NightTint = new(0.6, 0.7, 1.0);

    public float Ev100 { get; set; } = 15.0f;

    public Tonemapper Tonemapper { get; set; } = Tonemapper.Aces;

    public float AmbientScale { get; set; } = 1.0f;

    // Starlight and airglow on the dark side; auto exposure brings it up to a readable level.
    public float MinimumAmbientLux { get; set; } = 0.5f;

    public bool OverrideSun { get; set; }

    // Debug aid for inspecting models: the sun shines from over the camera's shoulder.
    public bool Headlight { get; set; }

    public float3 SunOverride { get; set; } = float3.Normalize(new float3(0.6f, 0.7f, 0.35f));

    // Degrees of azimuth from north and elevation above the camera's local horizon; for inspecting lighting.
    public float2? SunAzimuthElevation { get; set; }

    public float3 SunDirection { get; private set; } = float3.UnitY;

    public float3 SunIlluminance { get; private set; }

    public float SunCosAngularRadius { get; private set; } = 1;

    public float3 AmbientUp { get; private set; } = float3.UnitY;

    public float3 SkyIrradiance { get; private set; }

    public float3 GroundIrradiance { get; private set; }

    public float Exposure => 1.0f / (1.2f * MathF.Pow(2.0f, Ev100));

    public void Update(CelestialSystem system, double universalTime, double3 camera, double3 cameraForward, double3 cameraUp) {

        var sun = system.Sun;
        var direction = OverrideSun ? SpaceMath.ToDouble(SunOverride) : sun.DirectionAt(universalTime);

        if (Headlight) {

            direction = double3.Normalize(cameraUp * 0.6 - cameraForward + double3.Cross(cameraForward, cameraUp) * 0.4);

        }

        var body = system.DominantBodyAt(camera, universalTime);
        var offset = camera - system.StateAt(body.Index, universalTime).Position;
        var distance = Math.Max(offset.Length(), body.Radius);
        var up = offset / Math.Max(offset.Length(), 1e-9);

        if (SunAzimuthElevation is { } azel) {

            var east = double3.Normalize(double3.Cross(double3.UnitY, up));
            var north = double3.Cross(up, east);
            var azimuth = azel.X * Math.PI / 180;
            var elevation = azel.Y * Math.PI / 180;

            direction = (east * Math.Sin(azimuth) + north * Math.Cos(azimuth)) * Math.Cos(elevation) + up * Math.Sin(elevation);

        }

        SunDirection = SpaceMath.ToFloat(double3.Normalize(direction));
        SunIlluminance = sun.Color * (float)sun.Illuminance;
        SunCosAngularRadius = (float)Math.Cos(sun.AngularRadius);

        var sunHeight = double3.Dot(up, direction);

        AmbientUp = SpaceMath.ToFloat(up);

        var sky = double3.Zero;

        if (body.AtmosphereModel is { } atmosphere) {

            var altitude = distance - body.Radius;
            var density = altitude < atmosphere.Height ? body.SampleAtmosphere(altitude).Pressure / atmosphere.SurfacePressure : 0;
            var day = SmoothStep(-0.12, 0.25, sunHeight);

            sky = SkyTint * (sun.Illuminance * SkyFraction * density * day);

        }

        // Light reflected off the day side of the body below, falling off with its apparent size.
        var view = body.Radius / distance;
        var planetshine = PlanetAlbedo * sun.Illuminance * Math.Max(0, sunHeight * 0.5 + 0.25) * view * view;
        var ground = new double3(planetshine) + sky * PlanetAlbedo;
        var floor = NightTint * MinimumAmbientLux;

        SkyIrradiance = SpaceMath.ToFloat((sky + floor) * AmbientScale);
        GroundIrradiance = SpaceMath.ToFloat((ground + floor * 0.5) * AmbientScale);

    }

    private static double SmoothStep(double edge0, double edge1, double x) {

        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);

        return t * t * (3 - 2 * t);

    }

}
