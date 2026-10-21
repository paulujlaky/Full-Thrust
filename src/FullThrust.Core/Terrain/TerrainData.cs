namespace FullThrust.Core.Terrain;

// Serialized terrain parameters for a body. Heights in metres relative to the body's radius.
public sealed class TerrainData {

    // continental or cratered.
    public string Type { get; set; } = "continental";

    public int Seed { get; set; } = 1;

    // Heights below zero are covered by water; ground contact treats the sea surface as solid.
    public bool Ocean { get; set; }

    // Night-side city lights: luminance of a dense core in cd/m2, 0 for none.
    public double CityLights { get; set; }

    // Mean spacing in metres of the places on the town and city lattices.
    public double TownSpacing { get; set; } = 9000;

    public double CitySpacing { get; set; } = 70000;

    // Share of habitable land inside settled regions.
    public double Settled { get; set; } = 0.35;

    // Large-scale shape: features per unit radius for continents or maria.
    public double ContinentFrequency { get; set; } = 1.6;

    public double OceanDepth { get; set; } = 4000;

    public double MountainHeight { get; set; } = 4500;

    // Spectral detail: amplitude at the base wavelength, decaying as (wavelength ratio)^Roughness.
    public double DetailAmplitude { get; set; } = 300;

    public double DetailWavelength { get; set; } = 100_000;

    public double Roughness { get; set; } = 0.8;

    public double MareDepth { get; set; } = 1800;

    public double LargestCrater { get; set; } = 60_000;

    public double SmallestCrater { get; set; } = 40;

    public double CraterDensity { get; set; } = 0.55;

    public Dictionary<string, double[]> Colors { get; set; } = [];

    // Detail materials drawn over the palette colours up close, keyed by TerrainChannel name.
    public List<TerrainMaterialData> Materials { get; set; } = [];

    public List<SiteData> Sites { get; set; } = [];

}

public sealed class TerrainMaterialData {

    public string Channel { get; set; } = "";

    // Folder under data/textures/terrain holding the texture set.
    public string Texture { get; set; } = "";

    // Metres covered by one repeat of the texture.
    public double TileSize { get; set; } = 4;

}

// A flattened area, e.g. a launch complex. Angles in degrees.
public sealed class SiteData {

    public string Name { get; set; } = "";

    public double LatitudeDeg { get; set; }

    public double LongitudeDeg { get; set; }

    // Fully flat within Radius, blending back to natural terrain over Blend metres.
    public double Radius { get; set; } = 150;

    public double Blend { get; set; } = 800;

    // The pad never sits lower than this, keeping coastal sites dry.
    public double MinimumHeight { get; set; } = 5;

}
