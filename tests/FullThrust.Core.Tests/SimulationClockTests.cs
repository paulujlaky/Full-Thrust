using FullThrust.Core.Simulation;

namespace FullThrust.Core.Tests;

public class SimulationClockTests {

    private sealed class StubWarp : ITimeWarp {

        public double Rate { get; set; } = 1;

        public TimeWarpMode Mode { get; set; } = TimeWarpMode.Physics;

        public bool CanWarp => true;

    }

    [Fact]
    public void AccumulatesPartialFramesIntoFixedSteps() {

        var clock = new SimulationClock(new RealTimeWarp()) { FixedDeltaTime = 0.02 };

        Assert.Equal(0, clock.Advance(0.015));
        Assert.Equal(1, clock.Advance(0.015));

        var step = clock.NextFixedStep();

        Assert.Equal(0.02, step.DeltaTime, 1e-12);
        Assert.Equal(0.02, clock.UniversalTime, 1e-12);
        Assert.Equal(0.5, clock.InterpolationAlpha, 1e-9);

    }

    [Fact]
    public void ClampsStepsAndCountsDropped() {

        var clock = new SimulationClock(new RealTimeWarp()) { FixedDeltaTime = 0.01, MaxStepsPerFrame = 4 };

        Assert.Equal(4, clock.Advance(1.0));
        Assert.Equal(96, clock.DroppedSteps);

    }

    [Fact]
    public void PausedClockDoesNotAdvance() {

        var clock = new SimulationClock(new RealTimeWarp()) { Paused = true };

        Assert.Equal(0, clock.Advance(10));
        Assert.Equal(0, clock.UniversalTime);

    }

    [Fact]
    public void RailsWarpAdvancesTimeWithoutPhysicsSteps() {

        var warp = new StubWarp { Rate = 1000, Mode = TimeWarpMode.Rails };
        var clock = new SimulationClock(warp);

        Assert.Equal(0, clock.Advance(0.5));
        Assert.Equal(500, clock.UniversalTime, 1e-9);
        Assert.True(clock.FrameTime.OnRails);

    }

    [Fact]
    public void PhysicsWarpRunsMoreStepsPerFrame() {

        var warp = new StubWarp { Rate = 4 };
        var clock = new SimulationClock(warp) { FixedDeltaTime = 0.02, MaxStepsPerFrame = 100 };

        Assert.Equal(4, clock.Advance(0.02));

    }

    [Fact]
    public void NextFixedStepWithoutPendingStepsThrows() {

        var clock = new SimulationClock(new RealTimeWarp());

        Assert.Throws<InvalidOperationException>(() => clock.NextFixedStep());

    }

}
