using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Ecs;
using FullThrust.Core.Physics;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Orbits;

public sealed class CelestialBodySystem : ISystem {

    private readonly CelestialSystem _system;

    public CelestialBodySystem(CelestialSystem system) {

        _system = system;

    }

    public void Update(World world, in SimTime time) {

        _system.Update(time.UniversalTime);

        foreach (var row in world.Query<CelestialBodyRef, Transform>()) {

            var body = _system[row.C1.Index];

            row.C2.Position = _system.Position(body.Index);
            row.C2.Rotation = body.RotationAt(time.UniversalTime);

        }

    }

}

public sealed class OrbitPropagationSystem : ISystem {

    private const int MaxTransitionsPerFrame = 8;

    private readonly CelestialSystem _system;
    private readonly PatchedConicSolver _solver;

    private readonly OrbitEvents _events;

    public OrbitPropagationSystem(CelestialSystem system, PatchedConicSolver solver, OrbitEvents events) {

        _system = system;
        _solver = solver;
        _events = events;

    }

    public void Update(World world, in SimTime time) {

        var now = time.UniversalTime;
        var transforms = world.Store<Transform>();
        var velocities = world.Store<Velocity>();

        var physics = world.Store<PhysicsBody>();

        foreach (var row in world.Query<OrbitState>()) {

            // Physics craft own their transform and re-fit the conic themselves.
            if (physics.Has(row.Entity)) {

                continue;

            }

            ref var orbit = ref row.C1;

            for (var i = 0; i < MaxTransitionsPerFrame; i++) {

                var transition = _solver.FindNext(orbit.Body, orbit.Epoch, orbit.EpochTime, orbit.CurrentTime, now);

                if (!transition.Found) {

                    break;

                }

                var mu = _system[orbit.Body].GravitationalParameter;
                var atCrossing = _system.Propagator.Propagate(orbit.Epoch, mu, transition.Time - orbit.EpochTime);
                var rebased = _system.Rebase(atCrossing, orbit.Body, transition.ToBody, transition.Time);

                orbit.Rebase(transition.ToBody, rebased, transition.Time);

                _events.Record(new SoiTransitionEvent(row.Entity, transition.FromBody, transition.ToBody, transition.Time));

            }

            var bodyMu = _system[orbit.Body].GravitationalParameter;

            orbit.Current = _system.Propagator.Propagate(orbit.Epoch, bodyMu, now - orbit.EpochTime);
            orbit.CurrentTime = now;

            var frame = _system.StateAt(orbit.Body, now);

            ref var transform = ref transforms.TryGet(row.Entity, out var hasTransform);

            if (hasTransform) {

                transform.Position = frame.Position + orbit.Current.Position;

            }

            ref var velocity = ref velocities.TryGet(row.Entity, out var hasVelocity);

            if (hasVelocity) {

                velocity.Linear = frame.Velocity + orbit.Current.Velocity;

            }

        }

    }

}

// Fixed to a body's surface (in its rotating frame), e.g. launch pads and buildings.
public struct SurfaceAnchor {

    public int Body;

    public double3 Position;
    public doubleQuat Rotation;

}

// Runs after CelestialBodySystem so anchors follow the bodies' positions for this frame.
public sealed class SurfaceAnchorSystem : ISystem {

    private readonly CelestialSystem _system;

    public SurfaceAnchorSystem(CelestialSystem system) {

        _system = system;

    }

    public void Update(World world, in SimTime time) {

        foreach (var row in world.Query<SurfaceAnchor, Transform>()) {

            ref readonly var anchor = ref row.C1;

            var body = _system[anchor.Body];
            var state = _system.BodyFixedToWorld(anchor.Body, anchor.Position, time.UniversalTime);

            row.C2 = new Transform(state.Position, doubleQuat.Normalize(body.RotationAt(time.UniversalTime) * anchor.Rotation));

        }

    }

}
