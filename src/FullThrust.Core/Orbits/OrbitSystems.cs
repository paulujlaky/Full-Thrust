using FullThrust.Core.Bodies;
using FullThrust.Core.Ecs;
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

// Thrust never leaves the rails: each fixed step folds its delta-v into a fresh conic.
public sealed class OrbitalThrustSystem : ISystem {

    private readonly CelestialSystem _system;

    public OrbitalThrustSystem(CelestialSystem system) {

        _system = system;

    }

    public void Update(World world, in SimTime time) {

        foreach (var row in world.Query<OrbitState, ThrustAcceleration>()) {

            ref var orbit = ref row.C1;
            ref readonly var thrust = ref row.C2;

            if (!thrust.IsThrusting) {

                continue;

            }

            var mu = _system[orbit.Body].GravitationalParameter;
            var state = _system.Propagator.Propagate(orbit.Epoch, mu, time.UniversalTime - orbit.EpochTime);

            state.Velocity += thrust.Linear * time.DeltaTime;

            orbit.Epoch = state;
            orbit.EpochTime = time.UniversalTime;
            orbit.Version++;

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

        foreach (var row in world.Query<OrbitState>()) {

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
