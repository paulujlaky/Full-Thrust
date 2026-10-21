using Brutal.Numerics;
using FullThrust.Core.Atmosphere;
using FullThrust.Core.Orbits;
using FullThrust.Core.Simulation;
using FullThrust.Core.Terrain;

namespace FullThrust.Core.Bodies;

public sealed class CelestialBody {

    internal readonly List<CelestialBody> ChildList = [];

    public required int Index { get; init; }

    public required string Name { get; init; }

    public CelestialBody? Parent { get; init; }

    public required double Mass { get; init; }

    public required double GravitationalParameter { get; init; }

    public required double Radius { get; init; }

    public required double RotationPeriod { get; init; }

    public required double AxialTilt { get; init; }

    public required double InitialRotation { get; init; }

    public OrbitalElements Orbit { get; init; }

    // Two-body relative motion uses G(M + m), which is what gives the Moon its 27.32 day period.
    public double OrbitGravitationalParameter { get; init; }

    public double OrbitEpoch { get; init; }

    // Parent-relative state at OrbitEpoch, the starting point for rails propagation.
    public StateVector EpochState { get; init; }

    public double OrbitalPeriod { get; init; } = double.PositiveInfinity;

    public double SphereOfInfluence { get; init; } = double.PositiveInfinity;

    public AtmosphereData? Atmosphere { get; init; }

    public CloudData? Clouds { get; init; }

    public IAtmosphereModel? AtmosphereModel { get; init; }

    public ITerrain? Terrain { get; init; }

    public TerrainData? TerrainData { get; init; }

    public float3 DebugColor { get; init; }

    public IReadOnlyList<CelestialBody> Children => ChildList;

    public bool IsRoot => Parent is null;

    public bool HasAtmosphere => AtmosphereModel is not null;

    // Metres above the reference radius for a body-fixed position; zero without terrain.
    public double SurfaceHeight(double3 bodyFixed, double resolution = 1.0) => Terrain?.Height(double3.Normalize(bodyFixed), resolution) ?? 0;

    // What craft rest on: the terrain, or the sea surface above submerged terrain.
    public double GroundHeight(double3 bodyFixed, double resolution = 1.0) {

        var height = SurfaceHeight(bodyFixed, resolution);

        return Terrain is { HasOcean: true } ? Math.Max(height, 0) : height;

    }

    public double SurfaceGravity => GravitationalParameter / (Radius * Radius);

    // World-space spin vector; the atmosphere and surface co-rotate with it.
    public double3 AngularVelocity {

        get {

            if (RotationPeriod <= 0) {

                return double3.Zero;

            }

            var axis = doubleQuat.CreateFromAxisAngle(double3.UnitX, AxialTilt) * double3.UnitY;

            return axis * (2 * Math.PI / RotationPeriod);

        }

    }

    public bool InAtmosphere(double altitude) => AtmosphereModel is { } atmosphere && altitude < atmosphere.Height;

    public AtmosphereSample SampleAtmosphere(double altitude) => AtmosphereModel?.Sample(altitude) ?? AtmosphereSample.Vacuum;

    public doubleQuat RotationAt(double universalTime) {

        var spin = RotationPeriod > 0 ? InitialRotation + 2 * Math.PI * universalTime / RotationPeriod : InitialRotation;

        var tilt = doubleQuat.CreateFromAxisAngle(double3.UnitX, AxialTilt);
        var day = doubleQuat.CreateFromAxisAngle(double3.UnitY, spin % (2 * Math.PI));

        return doubleQuat.Normalize(tilt * day);

    }

    public override string ToString() => Name;

}
