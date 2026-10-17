using Brutal.Numerics;
using FullThrust.Core.Craft;
using FullThrust.Core.Diagnostics;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Tests;

public class HotPathAllocationTests {

    [Fact]
    public void SimulationFrameDoesNotAllocate() {

        var system = TestData.EarthMoon();
        var moon = system.Find("Moon")!;
        var solver = new PatchedConicSolver(system);
        var events = new OrbitEvents();
        var predictor = new TrajectoryPredictor(system, new PatchedConicSolver(system));

        var world = new World();
        var scheduler = new SystemScheduler();
        var warp = new TimeWarpController();
        var clock = new SimulationClock(warp);
        var origin = new FloatingOrigin { RecenterDistance = 10 };
        var stats = new FrameStats();

        warp.AddConstraint(new ThrustWarpConstraint(world));

        scheduler
            .Add(SystemPhase.FixedUpdate, new CraftControlSystem())
            .Add(SystemPhase.FixedUpdate, new OrbitalThrustSystem(system))
            .Add(SystemPhase.FixedUpdate, new KinematicsSystem())
            .Add(SystemPhase.LateUpdate, new CelestialBodySystem(system))
            .Add(SystemPhase.LateUpdate, new OrbitPropagationSystem(system, solver, events));

        Entity craft = default;

        for (var i = 0; i < 64; i++) {

            var entity = world.Create();
            var state = i % 2 == 0
                ? OrbitScenarios.LunarTransfer(system, moon, 6_571_000 + i * 1000, 0)
                : OrbitScenarios.Circular(system.Root.GravitationalParameter, 6_571_000 + i * 1000);

            world.Add(entity, new Transform(state.Position, doubleQuat.Identity));
            world.Add(entity, new Velocity { Angular = new double3(0, 0.1, 0) });
            world.Add(entity, OrbitState.Create(0, state, 0));
            world.Add(entity, new ThrustAcceleration());
            world.Add(entity, new CraftControl { Throttle = i % 3 == 0 ? 1 : 0, Pitch = 0.5f });
            world.Add(entity, new ControllableCraft { Mass = 1000, MaxThrust = 20000, MaxAngularAcceleration = 1, AngularDamping = 0.5 });

            craft = entity;

        }

        void Frame() {

            stats.BeginFrame();

            warp.Update();

            var steps = clock.Advance(1.0 / 60.0);

            for (var s = 0; s < steps; s++) {

                scheduler.Run(SystemPhase.FixedUpdate, world, clock.NextFixedStep());

            }

            scheduler.Run(SystemPhase.Update, world, clock.FrameTime);
            scheduler.Run(SystemPhase.LateUpdate, world, clock.FrameTime);

            predictor.Update(world.Get<OrbitState>(craft), clock.UniversalTime);
            origin.Update(world.Get<Transform>(craft).Position);

            stats.EndFrame(1.0 / 60.0);

        }

        for (var i = 0; i < 120; i++) {

            Frame();

        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 300; i++) {

            Frame();

        }

        foreach (var row in world.Query<CraftControl>()) {

            row.C1.Throttle = 0;

        }

        warp.SetLevel(7);

        for (var i = 0; i < 300; i++) {

            Frame();

        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.True(origin.ShiftCount > 0);
        Assert.Equal(TimeWarpMode.Rails, warp.Mode);
        Assert.True(events.TransitionCount > 0);

    }

}
