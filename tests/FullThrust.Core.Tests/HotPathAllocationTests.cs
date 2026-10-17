using Brutal.Numerics;
using FullThrust.Core.Craft;
using FullThrust.Core.Diagnostics;
using FullThrust.Core.Orbits;
using FullThrust.Core.Parts;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Tests;

public class HotPathAllocationTests {

    private const double FrameTime = 1.0 / 60.0;

    private sealed class Loop {

        private readonly FlightHarness _harness;

        private readonly FloatingOrigin _origin = new() { RecenterDistance = 10 };
        private readonly FrameStats _stats = new();
        private readonly TrajectoryPredictor _predictor;

        public Loop(FlightHarness harness) {

            _harness = harness;
            _predictor = new TrajectoryPredictor(harness.System, new PatchedConicSolver(harness.System));

        }

        public long ShiftCount => _origin.ShiftCount;

        public void Frame() {

            var craft = _harness.Focus.Craft;

            _stats.BeginFrame();
            _harness.Frame(FrameTime);
            _predictor.Update(_harness.World.Get<OrbitState>(craft), _harness.Clock.UniversalTime);
            _origin.Update(_harness.World.Get<Transform>(craft).Position);
            _stats.EndFrame(FrameTime);

        }

        public long Measure(int frames) {

            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < frames; i++) {

                Frame();

            }

            return GC.GetAllocatedBytesForCurrentThread() - before;

        }

    }

    [Fact]
    public void PoweredAscentDoesNotAllocate() {

        using var harness = new FlightHarness();

        var loop = new Loop(harness);
        var craft = harness.SpawnOnPad("harrier");

        ref var control = ref harness.Get<CraftControl>(craft);

        control.Throttle = 1;
        control.Sas = true;
        control.Pitch = 0.2f;

        harness.Stage(craft);
        loop.Measure(300);

        var allocated = loop.Measure(600);

        Assert.Equal(0, allocated);
        Assert.Equal(FlightMode.Physics, harness.Get<FlightState>(craft).Mode);
        Assert.True(harness.Get<FlightState>(craft).Thrust > 0);
        Assert.True(harness.PhysicsFrame.ShiftCount > 0);
        Assert.True(loop.ShiftCount > 0);

    }

    [Fact]
    public void OrbitalPhysicsAndRailsWarpDoNotAllocate() {

        using var harness = new FlightHarness();

        var loop = new Loop(harness);
        var moon = harness.System.Find("Moon")!;

        for (var i = 0; i < 12; i++) {

            var radius = harness.Earth.Radius + 200_000 + i * 1000;
            var state = i % 2 == 0
                ? OrbitScenarios.LunarTransfer(harness.System, moon, radius, 0)
                : OrbitScenarios.Circular(harness.Earth.GravitationalParameter, radius);

            CraftPlacement.InOrbit(harness.System, harness.Earth, state, 0).Spawn(harness.World, harness.Rocket("pinnacle"));

        }

        var craft = harness.SpawnInOrbit("pinnacle", 300_000);

        void Cycle(out long physics, out long rails) {

            ref var control = ref harness.Get<CraftControl>(craft);

            harness.Warp.Stop();

            control.Rcs = true;
            control.Sas = true;
            control.Translation = new float3(0, 1, 0);
            control.Roll = 1;

            physics = loop.Measure(200);

            control.Translation = default;
            control.Roll = 0;
            control.Rcs = false;

            physics += loop.Measure(150);

            Assert.Equal(FlightMode.Rails, harness.Get<FlightState>(craft).Mode);
            Assert.True(harness.Warp.SetLevel(7));

            rails = loop.Measure(300);

        }

        Cycle(out _, out _);
        Cycle(out var physicsBytes, out var railsBytes);

        Assert.Equal(0, physicsBytes);
        Assert.Equal(0, railsBytes);
        Assert.Equal(TimeWarpMode.Rails, harness.Warp.Mode);
        Assert.True(harness.Events.TransitionCount > 0);

    }

    // Staging creates craft and moves debris between physics and rails; with a reserved world that must not allocate.
    [Fact]
    public void StagingAndModeTransitionsDoNotAllocate() {

        static long FlyColossusStages() {

            using var harness = new FlightHarness();

            var craft = harness.SpawnInOrbit("colossus", 200_000);

            ref var control = ref harness.Get<CraftControl>(craft);

            control.Throttle = 1;
            control.Sas = true;

            harness.Run(0.5);

            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var stage = 0; stage < 4; stage++) {

                harness.Get<CraftStaging>(craft).Requested = true;
                harness.Run(2);

            }

            harness.Run(10);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(4, harness.Get<CraftStaging>(craft).NextStage);
            Assert.Equal(EngineStatus.Running, harness.Get<EngineState>(harness.FindPart<EngineState>(craft, "engine-service")).Status);
            Assert.Equal(4, harness.World.Query<Vessel>().Count);

            return allocated;

        }

        // The first pass pays one-time JIT and interop stub costs.
        FlyColossusStages();

        Assert.Equal(0, FlyColossusStages());

    }

}
