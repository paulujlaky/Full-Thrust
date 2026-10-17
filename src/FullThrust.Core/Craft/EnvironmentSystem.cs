using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Ecs;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Craft;

// Samples gravity, atmosphere and airflow for every craft at the start of a physics step.
public sealed class EnvironmentSystem : ISystem {

    private readonly CelestialSystem _system;

    public EnvironmentSystem(CelestialSystem system) {

        _system = system;

    }

    public void Update(World world, in SimTime time) {

        // Forces act over the step that ends at UniversalTime, so sample where it starts.
        var start = time.UniversalTime - time.DeltaTime;
        var masses = world.Store<CraftMass>();

        foreach (var row in world.Query<FlightState, Transform, Velocity>()) {

            ref var flight = ref row.C1;
            ref readonly var transform = ref row.C2;
            ref readonly var velocity = ref row.C3;

            ref readonly var mass = ref masses.TryGet(row.Entity, out var hasMass);

            var offset = hasMass ? transform.Rotation * mass.CenterOfMass : double3.Zero;
            var position = transform.Position + offset;
            var comVelocity = velocity.Linear + double3.Cross(velocity.Angular, offset);

            Sample(ref flight, position, comVelocity, start);

        }

    }

    // Terrain detail only matters close to the ground, so far-off samples use a coarse resolution.
    private static double SurfaceAltitude(CelestialBody body, double3 offset, double altitude, double universalTime) {

        if (body.Terrain is not { } terrain) {

            return altitude;

        }

        var resolution = Math.Max(1.0, (altitude - terrain.MaxHeight) * 0.01);
        var bodyFixed = doubleQuat.Conjugate(body.RotationAt(universalTime)) * offset;
        return altitude - body.GroundHeight(bodyFixed, resolution);

    }

    public void Sample(ref FlightState flight, double3 position, double3 velocity, double universalTime) {

        var body = _system.DominantBodyAt(position, universalTime);
        var frame = _system.StateAt(body.Index, universalTime);

        var r = position - frame.Position;
        var distance = r.Length();

        flight.Body = body.Index;
        flight.Altitude = distance - body.Radius;
        flight.SurfaceAltitude = SurfaceAltitude(body, r, flight.Altitude, universalTime);
        flight.Gravity = -r * (body.GravitationalParameter / (distance * distance * distance)) + _system.FrameAcceleration(body.Index, universalTime);
        flight.Ambient = body.SampleAtmosphere(flight.Altitude);

        flight.AirVelocity = velocity - frame.Velocity - double3.Cross(body.AngularVelocity, r);

        var airspeed = flight.AirVelocity.Length();

        flight.Mach = flight.Ambient.SpeedOfSound > 0 ? airspeed / flight.Ambient.SpeedOfSound : 0;
        flight.DynamicPressure = 0.5 * flight.Ambient.Density * airspeed * airspeed;
        flight.Drag = 0;

    }

}
