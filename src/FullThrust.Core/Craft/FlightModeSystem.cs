using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Physics;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Craft;

// Rails cannot model contact, drag or thrust, so craft needing any of those are handed to rigid-body physics.
public sealed class FlightModeSystem : ISystem {

    private const int MaxTransitions = 64;

    private readonly CelestialSystem _system;
    private readonly IRigidBodyWorld _bodies;
    private readonly PhysicsFrame _frame;
    private readonly FlightFocus _focus;

    private readonly ShapeElement[] _shape = new ShapeElement[CraftAssembly.MaxPartsPerCraft];

    private readonly Entity[] _toPhysics = new Entity[MaxTransitions];
    private readonly Entity[] _toRails = new Entity[MaxTransitions];
    private readonly Entity[] _toDestroy = new Entity[MaxTransitions];

    public FlightModeSystem(CelestialSystem system, IRigidBodyWorld bodies, PhysicsFrame frame, FlightFocus focus) {

        _system = system;
        _bodies = bodies;
        _frame = frame;
        _focus = focus;

    }

    // Beyond this distance from the focus, craft that still need physics are removed rather than simulated.
    public double PhysicsRange { get; set; } = 25_000;

    // Airless bodies have no atmosphere to flag a low pass, so low altitude does it instead.
    public double SurfaceProximity { get; set; } = 10_000;

    public double RailsDelay { get; set; } = 1.0;

    public long CraftDestroyed { get; private set; }

    public void Update(World world, in SimTime time) {

        var transforms = world.Store<Transform>();
        var physics = world.Store<PhysicsBody>();

        var focus = _focus.Craft;
        var hasFocus = world.IsAlive(focus) && transforms.Has(focus);
        var focusPosition = hasFocus ? transforms.Get(focus).Position : double3.Zero;

        int physicsCount = 0, railsCount = 0, destroyCount = 0;

        foreach (var row in world.Query<FlightState, Vessel, CraftControl>()) {

            ref var flight = ref row.C1;
            ref readonly var vessel = ref row.C2;
            ref readonly var control = ref row.C3;

            var entity = row.Entity;
            var isFocus = entity == focus;
            var nearSurface = NearSurface(in flight);
            var far = !isFocus && hasFocus && double3.DistanceSquared(transforms.Get(entity).Position, focusPosition) > PhysicsRange * PhysicsRange;

            var wants = nearSurface || vessel.SeparationTimer > 0 || flight.Thrust > 0 || WantsActuation(in vessel, in control);

            if (flight.Mode == FlightMode.Physics) {

                if (far && nearSurface) {

                    Push(_toDestroy, ref destroyCount, entity);

                } else if (far) {

                    Push(_toRails, ref railsCount, entity);

                } else if (!physics.Has(entity)) {

                    Push(_toPhysics, ref physicsCount, entity);

                } else if (wants) {

                    flight.RailsTimer = 0;

                } else if ((flight.RailsTimer += time.DeltaTime) >= RailsDelay) {

                    Push(_toRails, ref railsCount, entity);

                }

                continue;

            }

            if (!isFocus && flight.Altitude < 0) {

                Push(_toDestroy, ref destroyCount, entity);

            } else if (wants && far && nearSurface) {

                Push(_toDestroy, ref destroyCount, entity);

            } else if (wants && !far) {

                Push(_toPhysics, ref physicsCount, entity);

            }

        }

        for (var i = 0; i < destroyCount; i++) {

            DestroyCraft(world, _toDestroy[i]);

        }

        for (var i = 0; i < railsCount; i++) {

            EnterRails(world, _toRails[i]);

        }

        for (var i = 0; i < physicsCount; i++) {

            EnterPhysics(world, _toPhysics[i], time.UniversalTime - time.DeltaTime);

        }

    }

    public bool NearSurface(in FlightState flight) {

        var body = _system[flight.Body];

        return body.InAtmosphere(flight.Altitude) || flight.Altitude < SurfaceProximity;

    }

    private static bool WantsActuation(in Vessel vessel, in CraftControl control) {

        if (!vessel.Controllable) {

            return false;

        }

        return control.Throttle > 0 || (control.Rcs && (control.HasTranslationInput || control.HasRotationInput));

    }

    public void EnterPhysics(World world, Entity craft, double universalTime) {

        ref var flight = ref world.Get<FlightState>(craft);
        ref var transform = ref world.Get<Transform>(craft);
        ref var velocity = ref world.Get<Velocity>(craft);

        if (world.Has<PhysicsBody>(craft)) {

            return;

        }

        if (flight.Mode == FlightMode.Rails) {

            ref readonly var orbit = ref world.Get<OrbitState>(craft);

            var mu = _system[orbit.Body].GravitationalParameter;
            var relative = _system.Propagator.Propagate(orbit.Epoch, mu, universalTime - orbit.EpochTime);
            var frame = _system.StateAt(orbit.Body, universalTime);

            transform.Position = frame.Position + relative.Position;
            velocity.Linear = frame.Velocity + relative.Velocity;

        }

        var mass = CraftAssembly.ComputeMass(world, craft);
        var offset = transform.Rotation * mass.CenterOfMass;
        var comVelocity = velocity.Linear + double3.Cross(velocity.Angular, offset);

        if (_bodies.BodyCount == 0) {

            _frame.Reset(transform.Position, comVelocity);

        }

        var count = CraftAssembly.CollectShape(world, craft, _shape);

        var state = new RigidBodyState {

            Position = _frame.ToLocalPosition(transform.Position),
            Rotation = transform.Rotation,

            LinearVelocity = _frame.ToLocalVelocity(comVelocity),
            AngularVelocity = velocity.Angular,

        };

        var handle = _bodies.CreateDynamic(new ReadOnlySpan<ShapeElement>(_shape, 0, count), mass.Distribution, state);

        world.Add(craft, new PhysicsBody { Handle = handle });
        world.Get<CraftMass>(craft) = mass;
        world.Get<Vessel>(craft).ShapeDirty = false;

        flight.Mode = FlightMode.Physics;
        flight.RailsTimer = 0;
        flight.CenterOfMassVelocity = comVelocity;

    }

    // The orbit is re-fitted after every physics step, so the conic already matches the craft.
    public void EnterRails(World world, Entity craft) {

        ref var body = ref world.Store<PhysicsBody>().TryGet(craft, out var hasBody);

        if (hasBody) {

            _bodies.Destroy(body.Handle);
            world.Remove<PhysicsBody>(craft);

        }

        ref var flight = ref world.Get<FlightState>(craft);

        flight.Mode = FlightMode.Rails;
        flight.RailsTimer = 0;
        flight.Landed = false;
        flight.ProperAcceleration = double3.Zero;

    }

    public void DestroyAll(World world) {

        Span<Entity> craft = stackalloc Entity[MaxTransitions];

        var count = 0;

        foreach (var row in world.Query<Vessel>()) {

            if (count < craft.Length) {

                craft[count++] = row.Entity;

            }

        }

        for (var i = 0; i < count; i++) {

            DestroyCraft(world, craft[i]);

        }

    }

    public void DestroyCraft(World world, Entity craft) {

        ref var body = ref world.Store<PhysicsBody>().TryGet(craft, out var hasBody);

        if (hasBody) {

            _bodies.Destroy(body.Handle);

        }

        CraftAssembly.Destroy(world, craft);
        CraftDestroyed++;

    }

    private static void Push(Entity[] list, ref int count, Entity entity) {

        if (count < list.Length) {

            list[count++] = entity;

        }

    }

}
