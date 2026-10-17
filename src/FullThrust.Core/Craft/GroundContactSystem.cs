using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Ecs;
using FullThrust.Core.Physics;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Craft;

public struct GroundPatch {

    public int Body;

    // Grid centre on the reference sphere and its orientation in the body-fixed frame, so the patch turns with the planet.
    public double3 Anchor;
    public doubleQuat Orientation;

    public int Handle;

}

// A height-field patch of the terrain under the focus craft, rebuilt whenever the craft wanders away from its centre.
public sealed class GroundContactSystem : ISystem {

    public const int SampleCount = 40;

    private readonly CelestialSystem _system;
    private readonly IRigidBodyWorld _bodies;
    private readonly PhysicsFrame _frame;
    private readonly FlightFocus _focus;

    private readonly float[] _heights = new float[SampleCount * SampleCount];

    public GroundContactSystem(CelestialSystem system, IRigidBodyWorld bodies, PhysicsFrame frame, FlightFocus focus) {

        _system = system;
        _bodies = bodies;
        _frame = frame;
        _focus = focus;

    }

    // Metres between height samples; the patch spans (SampleCount - 1) * Spacing.
    public double Spacing { get; set; } = 6;

    public double ActivationAltitude { get; set; } = 3000;

    public double ReanchorDistance { get; set; } = 50;

    public Entity Ground { get; private set; }

    public long Rebuilds { get; private set; }

    public double LastRebuildMilliseconds { get; private set; }

    public double PatchSize => (SampleCount - 1) * Spacing;

    public void Update(World world, in SimTime time) {

        var focus = _focus.Craft;
        var active = world.IsAlive(focus) && world.Has<PhysicsBody>(focus) && world.Get<FlightState>(focus).SurfaceAltitude < ActivationAltitude;

        if (!active) {

            Remove(world);

            return;

        }

        var startTime = time.UniversalTime - time.DeltaTime;
        var flight = world.Get<FlightState>(focus);
        var position = world.Get<Transform>(focus).Position;

        if (!world.IsAlive(Ground)) {

            Create(world, flight.Body, position, startTime);

        }

        ref var patch = ref world.Get<GroundPatch>(Ground);

        var bodyFixed = _system.WorldToBodyFixed(patch.Body, position, startTime);
        var horizontal = bodyFixed - patch.Anchor * (double3.Dot(bodyFixed, patch.Anchor) / patch.Anchor.LengthSquared());

        if (patch.Body != flight.Body || horizontal.LengthSquared() > ReanchorDistance * ReanchorDistance) {

            var started = System.Diagnostics.Stopwatch.GetTimestamp();

            Anchor(ref patch, flight.Body, position, startTime);
            BuildHeights(in patch);

            _bodies.SetHeightField(patch.Handle, Grid());

            LastRebuildMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            var start = Pose(in patch, startTime);

            _bodies.SetState(patch.Handle, new RigidBodyState { Position = _frame.ToLocalPosition(start.Position), Rotation = start.Rotation });

        }

        // The patch is placed where it will be at the end of the step, in the frame as it will have coasted by then.
        var end = time.UniversalTime;
        var pose = Pose(in patch, end);
        var frameOrigin = _frame.Origin + _frame.Velocity * time.DeltaTime;

        _bodies.MoveKinematic(patch.Handle, pose.Position - frameOrigin, pose.Rotation, time.DeltaTime);

        world.Get<Transform>(Ground) = pose;

    }

    // Height of the patch surface above the anchor plane at a body-fixed position, for tests and debugging.
    public double PatchHeightAt(in GroundPatch patch, double3 bodyFixed) {

        var local = doubleQuat.Conjugate(patch.Orientation) * (bodyFixed - patch.Anchor);
        var half = PatchSize / 2;
        var x = Math.Clamp((local.X + half) / Spacing, 0, SampleCount - 1.001);
        var z = Math.Clamp((local.Z + half) / Spacing, 0, SampleCount - 1.001);

        var i = (int)x;
        var j = (int)z;
        var fx = x - i;
        var fz = z - j;

        var h00 = _heights[j * SampleCount + i];
        var h10 = _heights[j * SampleCount + i + 1];
        var h01 = _heights[(j + 1) * SampleCount + i];
        var h11 = _heights[(j + 1) * SampleCount + i + 1];

        return (h00 * (1 - fx) + h10 * fx) * (1 - fz) + (h01 * (1 - fx) + h11 * fx) * fz;

    }

    private HeightGrid Grid() => new(_heights, SampleCount, Spacing, new double3(-PatchSize / 2, 0, -PatchSize / 2));

    private void Create(World world, int body, double3 position, double universalTime) {

        var patch = new GroundPatch();

        Anchor(ref patch, body, position, universalTime);
        BuildHeights(in patch);

        var pose = Pose(in patch, universalTime);

        patch.Handle = _bodies.CreateKinematicHeightField(Grid(), _frame.ToLocalPosition(pose.Position), pose.Rotation);

        Ground = world.Create();

        world.Add(Ground, new Name("Ground"));
        world.Add(Ground, patch);
        world.Add(Ground, pose);

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
        var up = double3.Normalize(_system.WorldToBodyFixed(body, position, universalTime));

        patch.Body = body;
        patch.Anchor = up * celestial.Radius;
        patch.Orientation = SpaceMath.FromTo(double3.UnitY, up);

    }

    // Samples the ground under each grid point, measured along the anchor's up so curvature is baked in.
    private void BuildHeights(in GroundPatch patch) {

        var body = _system[patch.Body];
        var up = patch.Orientation * double3.UnitY;
        var half = PatchSize / 2;
        var resolution = Spacing * 0.5;

        for (var j = 0; j < SampleCount; j++) {

            for (var i = 0; i < SampleCount; i++) {

                var local = new double3(i * Spacing - half, 0, j * Spacing - half);
                var direction = double3.Normalize(patch.Anchor + patch.Orientation * local);
                var surface = direction * (body.Radius + body.GroundHeight(direction, resolution));

                _heights[j * SampleCount + i] = (float)double3.Dot(surface - patch.Anchor, up);

            }

        }

        Rebuilds++;

    }

    private Transform Pose(in GroundPatch patch, double universalTime) {

        var body = _system[patch.Body];
        var world = _system.BodyFixedToWorld(patch.Body, patch.Anchor, universalTime);

        return new Transform(world.Position, doubleQuat.Normalize(body.RotationAt(universalTime) * patch.Orientation));

    }

}
