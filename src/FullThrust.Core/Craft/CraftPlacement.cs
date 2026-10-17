using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Craft;

public readonly struct CraftPlacement {

    public readonly Transform Pose;
    public readonly Velocity Velocity;
    public readonly OrbitState Orbit;

    public CraftPlacement(in Transform pose, in Velocity velocity, in OrbitState orbit) {

        Pose = pose;
        Velocity = velocity;
        Orbit = orbit;

    }

    public Entity Spawn(World world, RocketPreset rocket) => CraftAssembly.Spawn(world, rocket, Pose, Velocity, Orbit);

    // Upright on the terrain, moving with it; latitude and longitude in radians.
    public static CraftPlacement OnSurface(CelestialSystem system, CelestialBody body, double latitude, double longitude, RocketPreset rocket, double universalTime, double clearance = 0.05) {

        var ground = body.GroundHeight(CelestialSystem.BodyFixedPoint(body, latitude, longitude, 0), 0.25);
        var site = CelestialSystem.BodyFixedPoint(body, latitude, longitude, ground + clearance - rocket.Bottom);
        var state = system.BodyFixedToWorld(body.Index, site, universalTime);
        var frame = system.StateAt(body.Index, universalTime);

        var up = double3.Normalize(state.Position - frame.Position);
        var relative = new StateVector(state.Position - frame.Position, state.Velocity - frame.Velocity);

        return new CraftPlacement(
            new Transform(state.Position, SpaceMath.FromTo(double3.UnitY, up)),
            new Velocity { Linear = state.Velocity },
            OrbitState.Create(body.Index, relative, universalTime));

    }

    // Nose prograde on a body-relative orbit.
    public static CraftPlacement InOrbit(CelestialSystem system, CelestialBody body, in StateVector relative, double universalTime) {

        var frame = system.StateAt(body.Index, universalTime);
        var rotation = OrbitCameraRig.LookRotation(double3.Normalize(relative.Position), double3.Normalize(relative.Velocity));

        return new CraftPlacement(
            new Transform(frame.Position + relative.Position, rotation),
            new Velocity { Linear = frame.Velocity + relative.Velocity },
            OrbitState.Create(body.Index, relative, universalTime));

    }

}
