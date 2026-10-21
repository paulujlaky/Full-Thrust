using FullThrust.Core.Terrain;

namespace FullThrust.Core.Bodies;

// Serialized definitions. SI units throughout; angles in degrees for readability.

public sealed class CelestialSystemData {

    public string Name { get; set; } = "";

    public SunData? Sun { get; set; }

    public List<CelestialBodyData> Bodies { get; set; } = [];

}

public sealed class CelestialBodyData {

    public string Name { get; set; } = "";

    public string? Parent { get; set; }

    public double Mass { get; set; }

    public double? GravitationalParameter { get; set; }

    public double Radius { get; set; }

    public double RotationPeriod { get; set; }

    public double AxialTiltDeg { get; set; }

    public double InitialRotationDeg { get; set; }

    public OrbitData? Orbit { get; set; }

    public AtmosphereData? Atmosphere { get; set; }

    public CloudData? Clouds { get; set; }

    public TerrainData? Terrain { get; set; }

    public double[] DebugColor { get; set; } = [0.6, 0.6, 0.6];

}

public sealed class OrbitData {

    public double SemiMajorAxis { get; set; }

    public double Eccentricity { get; set; }

    public double InclinationDeg { get; set; }

    public double LongitudeOfAscendingNodeDeg { get; set; }

    public double ArgumentOfPeriapsisDeg { get; set; }

    public double MeanAnomalyAtEpochDeg { get; set; }

    public double Epoch { get; set; }

}

public sealed class AtmosphereData {

    public double Height { get; set; }

    public double SurfacePressure { get; set; }

    public double MolarMass { get; set; }

    public List<AtmosphereLayerData> Layers { get; set; } = [];

    // Optical properties for rendering; null draws no sky.
    public ScatteringData? Scattering { get; set; }

}

// One cumulus layer; lengths in metres.
public sealed class CloudData {

    public double BaseAltitude { get; set; } = 1500;

    public double Thickness { get; set; } = 4000;

    // Fraction of the sky covered, 0 to 1.
    public double Coverage { get; set; } = 0.45;

    // Size of one weather system.
    public double WeatherScale { get; set; } = 60_000;

    // Size of the billows within a cloud.
    public double ShapeScale { get; set; } = 3000;

    public double DetailScale { get; set; } = 500;

    // Extinction per metre inside a dense cloud.
    public double Extinction { get; set; } = 0.04;

    public double WindSpeed { get; set; } = 8;

}

// Coefficients per metre at sea level, after Hillaire 2020 for Earth.
public sealed class ScatteringData {

    // Rendered shell above the surface; thinner than the physical atmosphere, where density is negligible.
    public double Height { get; set; } = 100_000;

    public double[] Rayleigh { get; set; } = [5.802e-6, 13.558e-6, 33.1e-6];

    public double RayleighScaleHeight { get; set; } = 8000;

    public double MieScattering { get; set; } = 3.996e-6;

    public double MieAbsorption { get; set; } = 4.4e-6;

    public double MieScaleHeight { get; set; } = 1200;

    // Henyey-Greenstein asymmetry of the aerosols.
    public double MieAnisotropy { get; set; } = 0.8;

    public double[] Ozone { get; set; } = [0.65e-6, 1.881e-6, 0.085e-6];

    // Ozone density falls linearly to zero this far either side of its peak.
    public double OzoneCenter { get; set; } = 25_000;

    public double OzoneHalfWidth { get; set; } = 15_000;

    public double[] GroundAlbedo { get; set; } = [0.3, 0.3, 0.3];

}

public sealed class AtmosphereLayerData {

    public double BaseAltitude { get; set; }

    public double BaseTemperature { get; set; }

    public double LapseRate { get; set; }

}
