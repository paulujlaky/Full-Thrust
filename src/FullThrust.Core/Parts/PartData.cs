namespace FullThrust.Core.Parts;

// Serialized part catalog. SI units throughout; angles in degrees for readability.

public sealed class PartCatalogData {

    public List<ResourceData> Resources { get; set; } = [];

    public List<PartData> Parts { get; set; } = [];

}

public sealed class ResourceData {

    public string Name { get; set; } = "";

    public double Density { get; set; }

}

public sealed class PartData {

    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Category { get; set; } = "";

    public double Mass { get; set; }

    public double Length { get; set; }

    public double Diameter { get; set; }

    public string Shape { get; set; } = "cylinder";

    public double DragCoefficient { get; set; } = 0.8;

    public double[] Color { get; set; } = [0.8, 0.8, 0.8];

    public PartModelData? Model { get; set; }

    public TankData? Tank { get; set; }

    public EngineData? Engine { get; set; }

    public RcsData? Rcs { get; set; }

    public DecouplerData? Decoupler { get; set; }

    public CommandData? Command { get; set; }

}

public sealed class PartModelData {

    // Relative to data/models.
    public string File { get; set; } = "";

    // Applied before fitting, to bring the model to Y up (nose towards +Y).
    public double[] RotationDeg { get; set; } = [0, 0, 0];

    // none, uniform, stretch or length; fitting targets the part's diameter and length.
    public string Fit { get; set; } = "none";

    public double[] Offset { get; set; } = [0, 0, 0];

}

public sealed class TankData {

    public List<PropellantLoadData> Propellants { get; set; } = [];

    public double UllageFraction { get; set; } = 0.03;

    public double Pressure { get; set; }

    public double PressurantMass { get; set; }

}

public sealed class PropellantLoadData {

    public string Resource { get; set; } = "";

    public double Mass { get; set; }

}

public sealed class EngineData {

    public string Fuel { get; set; } = "";

    public string? Oxidizer { get; set; }

    public double MixtureRatio { get; set; }

    public double VacuumThrust { get; set; }

    public double VacuumIsp { get; set; }

    public double SeaLevelIsp { get; set; }

    public double MinThrottle { get; set; } = 1.0;

    public double GimbalRangeDeg { get; set; }

    public double GimbalRateDeg { get; set; } = 10;

    public string Feed { get; set; } = "turbopump";

    public double SpoolUpTime { get; set; } = 2.0;

    public double SpoolDownTime { get; set; } = 0.5;

    public double MinInletPressure { get; set; }

    public double FeedPressure { get; set; }

    public int Ignitions { get; set; } = 1;

    public bool RequiresUllage { get; set; } = true;

}

public sealed class RcsData {

    public string Propellant { get; set; } = "";

    public double NozzleThrust { get; set; }

    public double VacuumIsp { get; set; }

    public double SeaLevelIsp { get; set; }

    // Force directions on the craft in part space, where +X points away from the parent.
    public List<double[]> Nozzles { get; set; } = [];

}

public sealed class DecouplerData {

    public double EjectionImpulse { get; set; }

}

public sealed class CommandData {

    public double ReactionWheelTorque { get; set; }

}
