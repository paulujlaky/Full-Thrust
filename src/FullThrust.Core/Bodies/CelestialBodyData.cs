namespace FullThrust.Core.Bodies;

// Serialized definitions. SI units throughout; angles in degrees for readability.

public sealed class CelestialSystemData {

    public string Name { get; set; } = "";

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

}

public sealed class AtmosphereLayerData {

    public double BaseAltitude { get; set; }

    public double BaseTemperature { get; set; }

    public double LapseRate { get; set; }

}
