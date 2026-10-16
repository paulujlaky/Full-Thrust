using Brutal.Numerics;
using FullThrust.Core.Craft;
using FullThrust.Core.Diagnostics;
using FullThrust.Core.Ecs;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Tests;

public class HotPathAllocationTests {

    [Fact]
    public void SimulationFrameDoesNotAllocate() {

        var world = new World();
        var scheduler = new SystemScheduler();
        var clock = new SimulationClock(new RealTimeWarp());
        var origin = new FloatingOrigin { RecenterDistance = 10 };
        var stats = new FrameStats();

        scheduler
            .Add(SystemPhase.FixedUpdate, new CraftControlSystem())
            .Add(SystemPhase.FixedUpdate, new KinematicsSystem());

        Entity craft = default;

        for (var i = 0; i < 256; i++) {

            var entity = world.Create();

            world.Add(entity, new Transform(new double3(6_471_000 + i, 0, 0), doubleQuat.Identity));
            world.Add(entity, new Velocity { Linear = new double3(0, 7800, 0), Angular = new double3(0, 0.1, 0) });
            world.Add(entity, new CraftControl { Throttle = 1, Pitch = 0.5f });
            world.Add(entity, new ControllableCraft { Mass = 1000, MaxThrust = 20000, MaxAngularAcceleration = 1, AngularDamping = 0.5 });

            craft = entity;

        }

        void Frame() {

            stats.BeginFrame();

            var steps = clock.Advance(1.0 / 60.0);

            for (var s = 0; s < steps; s++) {

                scheduler.Run(SystemPhase.FixedUpdate, world, clock.NextFixedStep());

            }

            scheduler.Run(SystemPhase.Update, world, clock.FrameTime);
            origin.Update(world.Get<Transform>(craft).Position);

            stats.EndFrame(1.0 / 60.0);

        }

        for (var i = 0; i < 120; i++) {

            Frame();

        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 600; i++) {

            Frame();

        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.True(origin.ShiftCount > 0);

    }

}
