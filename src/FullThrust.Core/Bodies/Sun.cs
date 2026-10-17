using Brutal.Numerics;

namespace FullThrust.Core.Bodies;

public sealed class SunData {

    public double IlluminanceLux { get; set; } = 120_000;

    public double[] Color { get; set; } = [1.0, 0.97, 0.92];

    public double YearLength { get; set; } = 31_558_149.8;

    public double LongitudeAtEpochDeg { get; set; }

    public double AngularDiameterDeg { get; set; } = 0.53;

}

// The sun is far enough away to be a direction; the world reference plane is the ecliptic.
public sealed class Sun {

    public double Illuminance { get; init; } = 120_000;

    public float3 Color { get; init; } = new(1.0f, 0.97f, 0.92f);

    public double YearLength { get; init; } = 31_558_149.8;

    public double LongitudeAtEpoch { get; init; }

    public double AngularRadius { get; init; } = 0.53 * Math.PI / 360;

    public static Sun FromData(SunData? data) {

        if (data is null) {

            return new Sun();

        }

        var color = data.Color.Length >= 3 ? new float3((float)data.Color[0], (float)data.Color[1], (float)data.Color[2]) : new float3(1);

        return new Sun {

            Illuminance = data.IlluminanceLux,
            Color = color,
            YearLength = data.YearLength,
            LongitudeAtEpoch = data.LongitudeAtEpochDeg * Math.PI / 180,
            AngularRadius = data.AngularDiameterDeg * Math.PI / 360,

        };

    }

    // Unit vector towards the sun, matching the longitude convention of CelestialSystem.BodyFixedPoint.
    public double3 DirectionAt(double universalTime) {

        var longitude = LongitudeAtEpoch;

        if (YearLength > 0) {

            longitude += 2 * Math.PI * (universalTime / YearLength % 1.0);

        }

        return new double3(Math.Cos(longitude), 0, -Math.Sin(longitude));

    }

}
