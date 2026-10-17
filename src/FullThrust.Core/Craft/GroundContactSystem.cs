using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Ecs;
using FullThrust.Core.Physics;
using FullThrust.Core.Rendering;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Craft;

public struct GroundPatch {

    public int Body;

    // Box centre and orientation in the body-fixed frame, so the patch turns with the planet.
    public double3 Anchor;
    public doubleQuat Orientation;

    public int Handle;

}

// Until terrain exists, a kinematic slab turning with the planet stands in for the ground under the focus craft.
public sealed class GroundContactSystem : ISystem {

    private readonly CelestialSystem _system;
    private readonly IRigidBodyWorld _bodies;
    private readonly PhysicsFrame _frame;
    private readonly FlightFocus _focus;

    private readonly ShapeElement[] _shape = new ShapeElement[1];

    public GroundContactSystem(CelestialSystem system, IRigidBodyWorld bodies, PhysicsFrame frame, FlightFocus focus) {

        _system = system;
        _bodies = bodies;
        _frame = frame;
        _focus = focus;

    }

    public double3 HalfExtents { get; set; } = new(1000, 10, 1000);

    public double ActivationAltitude { get; set; } = 3000;

    public double ReanchorDistance { get; set; } = 400;

    public Entity Ground { get; private set; }

    public void Update(World world, in SimTime time) {

        var focus = _focus.Craft;
        var active = world.IsAlive(focus) && world.Has<PhysicsBody>(focus) && world.Get<FlightState>(focus).Altitude < ActivationAltitude;

        if (!active) {

            Remove(world);

            return;

        }

        var start = time.UniversalTime - time.DeltaTime;
        var flight = world.Get<FlightState>(focus);
        var position = world.Get<Transform>(focus).Position;

        if (!world.IsAlive(Ground)) {

            Create(world, flight.Body, position, start);

        }

        ref var patch = ref world.Get<GroundPatch>(Ground);

        var bodyFixed = ToBodyFixed(patch.Body, position, start);
        var horizontal = bodyFixed - patch.Anchor * (double3.Dot(bodyFixed, patch.Anchor) / patch.Anchor.LengthSquared());

        if (patch.Body != flight.Body || horizontal.LengthSquared() > ReanchorDistance * ReanchorDistance) {

            Anchor(ref patch, flight.Body, position, start);
            Teleport(in patch, start);

        }

        // Kinematic targets are for the end of the step; the frame will have coasted by then.
        var end = time.UniversalTime;
        var pose = Pose(in patch, end);
        var frameOrigin = _frame.Origin + _frame.Velocity * time.DeltaTime;

        _bodies.MoveKinematic(patch.Handle, pose.Position - frameOrigin, pose.Rotation, time.DeltaTime);

        world.Get<Transform>(Ground) = pose;

    }

    private void Create(World world, int body, double3 position, double universalTime) {

        var patch = new GroundPatch();

        Anchor(ref patch, body, position, universalTime);

        var pose = Pose(in patch, universalTime);

        _shape[0] = new ShapeElement(ShapePrimitive.Box, double3.Zero, doubleQuat.Identity, HalfExtents);
        patch.Handle = _bodies.CreateKinematic(_shape, _frame.ToLocalPosition(pose.Position), pose.Rotation);

        var extents = HalfExtents * 2;

        Ground = world.Create();

        world.Add(Ground, new Name("Ground"));
        world.Add(Ground, patch);
        world.Add(Ground, pose);
        world.Add(Ground, new DebugShape { Kind = ShapeKind.Box, Scale = new float3((float)extents.X, (float)extents.Y, (float)extents.Z), Color = new float4(0.32f, 0.36f, 0.30f, 1) });

    }

    private void Remove(World world) {

        if (!world.IsAlive(Ground)) {

            return;

        }

        _bodies.Destroy(world.Get<GroundPatch>(Ground).Handle);
        world.Destroy(Ground);

        Ground = Entity.Null;

    }

    private void Anchor(ref GroundPatch patch, int body, double3 position, double universalTime) {

        var celestial = _system[body];
        var up = double3.Normalize(ToBodyFixed(body, position, universalTime));

        patch.Body = body;
        patch.Anchor = up * (celestial.Radius - HalfExtents.Y);
        patch.Orientation = SpaceMath.FromTo(double3.UnitY, up);

    }

    private void Teleport(in GroundPatch patch, double universalTime) {

        var pose = Pose(in patch, universalTime);

        _bodies.SetState(patch.Handle, new RigidBodyState { Position = _frame.ToLocalPosition(pose.Position), Rotation = pose.Rotation });

    }

    private Transform Pose(in GroundPatch patch, double universalTime) {

        var body = _system[patch.Body];
        var world = _system.BodyFixedToWorld(patch.Body, patch.Anchor, universalTime);

        return new Transform(world.Position, doubleQuat.Normalize(body.RotationAt(universalTime) * patch.Orientation));

    }

    private double3 ToBodyFixed(int body, double3 position, double universalTime) {

        var celestial = _system[body];
        var offset = position - _system.StateAt(body, universalTime).Position;

        return doubleQuat.Conjugate(celestial.RotationAt(universalTime)) * offset;

    }

}
