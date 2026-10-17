using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Craft;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Parts;
using FullThrust.Core.Physics;
using FullThrust.Core.Simulation;
using FullThrust.Physics.Jolt;

namespace FullThrust.Core.Tests;

// The full flight stack as the game wires it, minus rendering and input.
internal sealed class FlightHarness : IDisposable {

    public const double Step = 1.0 / 50.0;

    public const double LaunchLatitude = 28.5 * Math.PI / 180;

    public FlightHarness() {

        System = TestData.EarthMoon();
        Parts = TestData.Parts();
        Rockets = TestData.Rockets(Parts);

        Clock = new SimulationClock(Warp);
        World.Reserve(1024);
        Bodies = new JoltRigidBodyWorld();
        Flight = new FlightPipeline(System, Bodies, PhysicsFrame, Focus, Events);

        Warp.AddConstraint(new ThrustWarpConstraint(World));
        Warp.AddConstraint(new PhysicsWarpConstraint(World));

        Flight.AddFixedSystems(Scheduler)
            .Add(SystemPhase.LateUpdate, new CelestialBodySystem(System))
            .Add(SystemPhase.LateUpdate, new OrbitPropagationSystem(System, new PatchedConicSolver(System), Events))
            .Add(SystemPhase.LateUpdate, Flight.PartTransforms);

    }

    public CelestialSystem System { get; }

    public PartCatalog Parts { get; }

    public RocketCatalog Rockets { get; }

    public World World { get; } = new();

    public SystemScheduler Scheduler { get; } = new();

    public TimeWarpController Warp { get; } = new();

    public SimulationClock Clock { get; }

    public JoltRigidBodyWorld Bodies { get; }

    public PhysicsFrame PhysicsFrame { get; } = new();

    public FlightFocus Focus { get; } = new();

    public OrbitEvents Events { get; } = new();

    public FlightPipeline Flight { get; }

    public CelestialBody Earth => System.Root;

    public RocketPreset Rocket(string id) => Rockets.Find(id) ?? throw new ArgumentException(id);

    public Entity SpawnOnPad(string rocket) {

        var preset = Rocket(rocket);

        Focus.Craft = CraftPlacement.OnSurface(System, Earth, LaunchLatitude, 0, preset, Clock.UniversalTime).Spawn(World, preset);

        return Focus.Craft;

    }

    public Entity SpawnInOrbit(string rocket, double altitude) {

        var preset = Rocket(rocket);
        var state = OrbitScenarios.Circular(Earth.GravitationalParameter, Earth.Radius + altitude);

        Focus.Craft = CraftPlacement.InOrbit(System, Earth, state, Clock.UniversalTime).Spawn(World, preset);

        return Focus.Craft;

    }

    public void Frame(double realDeltaTime = Step) {

        var steps = Clock.Advance(realDeltaTime);

        Scheduler.Run(SystemPhase.Update, World, Clock.FrameTime);
        Warp.Update();

        for (var i = 0; i < steps; i++) {

            Scheduler.Run(SystemPhase.FixedUpdate, World, Clock.NextFixedStep());

        }

        Scheduler.Run(SystemPhase.LateUpdate, World, Clock.FrameTime);

    }

    // Whole fixed steps, so tests are independent of accumulator rounding.
    public void Run(double seconds) {

        var steps = (int)Math.Round(seconds / Clock.FixedDeltaTime);

        for (var i = 0; i < steps; i++) {

            Frame(Clock.FixedDeltaTime * 1.0000001);

        }

    }

    public void Stage(Entity craft) {

        World.Get<CraftStaging>(craft).Requested = true;
        Run(Step);

    }

    public ref T Get<T>(Entity entity) where T : struct => ref World.Get<T>(entity);

    public Entity FindPart<T>(Entity craft, string partId) where T : struct {

        foreach (var row in World.Query<T, Part>()) {

            if (row.C2.Craft == craft && row.C2.Definition.Id == partId) {

                return row.Entity;

            }

        }

        throw new InvalidOperationException($"{partId} not found on craft.");

    }

    public Entity FindPart<T>(string partId) where T : struct {

        foreach (var row in World.Query<T, Part>()) {

            if (row.C2.Definition.Id == partId) {

                return row.Entity;

            }

        }

        throw new InvalidOperationException($"{partId} not found.");

    }

    public double3 ComPosition(Entity craft) {

        ref readonly var transform = ref World.Get<Space.Transform>(craft);

        return transform.Position + transform.Rotation * World.Get<CraftMass>(craft).CenterOfMass;

    }

    public void Dispose() => Bodies.Dispose();

}
