using Brutal.Numerics;
using FullThrust.Core.Orbits;
using FullThrust.Core.Simulation;

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

    public float3 DebugColor { get; init; }

    public IReadOnlyList<CelestialBody> Children => ChildList;

    public bool IsRoot => Parent is null;

    public bool HasAtmosphere => Atmosphere is not null;

    public doubleQuat RotationAt(double universalTime) {

        var spin = RotationPeriod > 0 ? InitialRotation + 2 * Math.PI * universalTime / RotationPeriod : InitialRotation;

        var tilt = doubleQuat.CreateFromAxisAngle(double3.UnitX, AxialTilt);
        var day = doubleQuat.CreateFromAxisAngle(double3.UnitY, spin % (2 * Math.PI));

        return doubleQuat.Normalize(tilt * day);

    }

    public override string ToString() => Name;

}
