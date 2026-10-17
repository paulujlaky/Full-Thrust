using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Physics;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Craft;

// Re-fitting the conic every step keeps prediction, the HUD and a later return to rails consistent.
public sealed class RigidBodySystem : ISystem {

    // Proper acceleration along the nose above this keeps propellant at the tank outlets.
    public const double SettlingAcceleration = 0.03;

    private readonly CelestialSystem _system;
    private readonly IRigidBodyWorld _bodies;
    private readonly PhysicsFrame _frame;
    private readonly FlightFocus _focus;
    private readonly OrbitEvents _events;

    public RigidBodySystem(CelestialSystem system, IRigidBodyWorld bodies, PhysicsFrame frame, FlightFocus focus, OrbitEvents events) {

        _system = system;
        _bodies = bodies;
        _frame = frame;
        _focus = focus;
        _events = events;

    }

    public long Steps { get; private set; }

    public void Update(World world, in SimTime time) {

        var dt = time.DeltaTime;
        var flights = world.Store<FlightState>();

        foreach (var row in world.Query<PhysicsBody, CraftForces, CraftMass>()) {

            ref var forces = ref row.C2;

            var gravity = flights.Get(row.Entity).Gravity * row.C3.Mass;

            _bodies.AddForceAndTorque(row.C1.Handle, forces.Force + gravity, forces.Torque);

            forces = default;

        }

        // Forces on rails craft (e.g. a separation kick before the mode switch) must not linger.
        foreach (var row in world.Query<CraftForces>()) {

            row.C1 = default;

        }

        if (_bodies.BodyCount == 0) {

            return;

        }

        _bodies.Step(dt);
        _frame.Advance(dt);

        Steps++;

        ReadBack(world, time.UniversalTime, dt);
        Recenter(world);

    }

    private void ReadBack(World world, double now, double dt) {

        var flights = world.Store<FlightState>();
        var orbits = world.Store<OrbitState>();
        var vessels = world.Store<Vessel>();

        foreach (var row in world.Query<PhysicsBody, Transform, Velocity>()) {

            ref var transform = ref row.C2;
            ref var velocity = ref row.C3;

            var state = _bodies.GetState(row.C1.Handle);

            transform.Position = _frame.ToWorldPosition(state.Position);
            transform.Rotation = state.Rotation;

            var com = _frame.ToWorldPosition(state.CenterOfMass);
            var comVelocity = _frame.ToWorldVelocity(state.LinearVelocity);

            velocity.Angular = state.AngularVelocity;
            velocity.Linear = comVelocity + double3.Cross(state.AngularVelocity, transform.Position - com);

            ref var flight = ref flights.Get(row.Entity);

            flight.ProperAcceleration = (comVelocity - flight.CenterOfMassVelocity) / dt - flight.Gravity;
            flight.CenterOfMassVelocity = comVelocity;

            UpdateUllage(ref flight, transform.Rotation * double3.UnitY, dt);

            ref var orbit = ref orbits.TryGet(row.Entity, out var hasOrbit);

            if (hasOrbit) {

                Refit(row.Entity, ref orbit, ref flight, in transform, in velocity, vessels.Get(row.Entity).Bottom, now);

            }

        }

    }

    private static void UpdateUllage(ref FlightState flight, double3 nose, double dt) {

        var axial = double3.Dot(flight.ProperAcceleration, nose);

        if (axial > SettlingAcceleration) {

            flight.UllageSettle = Math.Min(1, flight.UllageSettle + dt / 0.5);

        } else if (axial < -SettlingAcceleration) {

            flight.UllageSettle = Math.Max(0, flight.UllageSettle - dt / 1.0);

        } else {

            flight.UllageSettle = Math.Max(0, flight.UllageSettle - dt / 3.0);

        }

    }

    private void Refit(Entity entity, ref OrbitState orbit, ref FlightState flight, in Transform transform, in Velocity velocity, double bottom, double now) {

        var body = _system.DominantBodyAt(transform.Position, now);
        var frame = _system.StateAt(body.Index, now);

        var relative = new StateVector(transform.Position - frame.Position, velocity.Linear - frame.Velocity);

        if (orbit.Body != body.Index) {

            _events.Record(new SoiTransitionEvent(entity, orbit.Body, body.Index, now));

        }

        orbit.Rebase(body.Index, relative, now);

        var up = double3.Normalize(relative.Position);
        var clearance = relative.Position.Length() - body.Radius + double3.Dot(transform.Rotation * new double3(0, bottom, 0), up);
        var surfaceVelocity = relative.Velocity - double3.Cross(body.AngularVelocity, relative.Position);

        flight.Landed = clearance < 0.5 && surfaceVelocity.LengthSquared() < 1.0;

    }

    private void Recenter(World world) {

        var focus = _focus.Craft;

        if (!world.IsAlive(focus) || !world.Has<PhysicsBody>(focus)) {

            foreach (var row in world.Query<PhysicsBody, FlightState>()) {

                focus = row.Entity;

                break;

            }

        }

        if (!world.IsAlive(focus) || !world.Has<PhysicsBody>(focus)) {

            return;

        }

        _frame.Recenter(world.Get<Transform>(focus).Position, world.Get<FlightState>(focus).CenterOfMassVelocity, _bodies);

    }

}
