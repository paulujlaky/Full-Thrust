using Brutal.Numerics;

namespace FullThrust.Core.Terrain;

public struct TerrainSample {

    // Metres above the body's reference radius (sea level when there is an ocean).
    public double Height;

    // 0..1 inputs for surface colouring: wetness on continents, maria on cratered bodies.
    public float Moisture;

    // 0..1 local feature strength: mountain cores, crater rims and ejecta.
    public float Feature;

}

// Height field over a body's surface, sampled by body-fixed unit direction.
public interface ITerrain {

    bool HasOcean { get; }

    // Conservative bounds for culling and LOD.
    double MinHeight { get; }
    double MaxHeight { get; }

    IReadOnlyList<TerrainSite> Sites { get; }

    // Resolution in metres: detail finer than this is skipped (LOD); physics uses about a metre.
    TerrainSample Sample(double3 direction, double resolution);

    double Height(double3 direction, double resolution = 1.0);

}

public readonly struct TerrainSite {

    public readonly string Name;

    public readonly double Latitude;
    public readonly double Longitude;

    // Body-fixed unit direction to the site centre.
    public readonly double3 Direction;

    public readonly double Height;
    public readonly double Radius;
    public readonly double Blend;

    public TerrainSite(string name, double latitude, double longitude, double3 direction, double height, double radius, double blend) {

        Name = name;
        Latitude = latitude;
        Longitude = longitude;
        Direction = direction;
        Height = height;
        Radius = radius;
        Blend = blend;

    }

}
