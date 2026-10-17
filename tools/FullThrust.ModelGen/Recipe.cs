using Brutal.Numerics;

namespace FullThrust.ModelGen;

public sealed class RecipeFile {

    public List<Recipe> Models { get; set; } = [];

}

// One generated model. Unused fields are ignored by the recipe type.
public sealed class Recipe {

    // Relative to data/models.
    public string Output { get; set; } = "";

    // tank, pod, ring, rcs, cluster, crop, launchpad.
    public string Type { get; set; } = "";

    // Takes length and diameter from data/parts/parts.json.
    public string? Part { get; set; }

    public double Length { get; set; }

    public double Diameter { get; set; }

    public double TopDiameter { get; set; }

    public double[] Paint { get; set; } = [0.85, 0.85, 0.83];

    public double[] Accent { get; set; } = [0.05, 0.05, 0.05];

    public double PaintMetallic { get; set; }

    public double PaintRoughness { get; set; } = 0.45;

    // solid, roll, radiator, corrugated.
    public string Pattern { get; set; } = "solid";

    public List<Band> Bands { get; set; } = [];

    public bool Raceway { get; set; } = true;

    public int Windows { get; set; }

    public double HeatShieldDepth { get; set; } = 0.25;

    public double NoseHeight { get; set; }

    public bool DockingTunnel { get; set; }

    // crop and cluster: a source model relative to data/models.
    public string? Source { get; set; }

    public double[] SourceRotationDeg { get; set; } = [0, 0, 0];

    public double CropMinY { get; set; } = double.NegativeInfinity;

    public double CropMaxY { get; set; } = double.PositiveInfinity;

    public int RingEngines { get; set; }

    public bool CenterEngine { get; set; }

    public double RingRadius { get; set; }

    public double EngineDiameter { get; set; }

    public double EngineLength { get; set; }

    public int Seed { get; set; } = 1;

    public float3 PaintColor => Color(Paint);

    public float3 AccentColor => Color(Accent);

    private static float3 Color(double[] c) => c.Length >= 3 ? new float3((float)c[0], (float)c[1], (float)c[2]) : new float3(0.8f);

}

// A painted or bare-metal ring on a tank, in metres from the bottom.
public sealed class Band {

    public double From { get; set; }

    public double To { get; set; }

    // accent or metal.
    public string Material { get; set; } = "accent";

}
