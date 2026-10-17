using FullThrust.Core.Craft;
using FullThrust.Core.Ecs;
using FullThrust.Core.Simulation;

namespace FullThrust.Core.Tests;

public class TimeWarpTests {

    [Fact]
    public void OneXIsPhysicsAndHigherLevelsAreRails() {

        var warp = new TimeWarpController();

        Assert.Equal(1, warp.Rate);
        Assert.Equal(TimeWarpMode.Physics, warp.Mode);

        Assert.True(warp.Increase());
        Assert.Equal(5, warp.Rate);
        Assert.Equal(TimeWarpMode.Rails, warp.Mode);

        warp.Stop();
        Assert.Equal(TimeWarpMode.Physics, warp.Mode);

    }

    [Fact]
    public void LevelsClampToTable() {

        var warp = new TimeWarpController();

        warp.SetLevel(100);
        Assert.Equal(TimeWarpController.DefaultRailsRates[^1], warp.Rate);

        warp.SetLevel(-5);
        Assert.Equal(1, warp.Rate);

    }

    [Fact]
    public void ThrustBlocksRailsWarp() {

        var world = new World();
        var craft = world.Create();

        world.Add(craft, new CraftControl { Throttle = 0.5f });

        var warp = new TimeWarpController();

        warp.AddConstraint(new ThrustWarpConstraint(world));

        Assert.False(warp.CanWarp);
        Assert.False(warp.Increase());
        Assert.Equal(1, warp.Rate);
        Assert.NotNull(warp.BlockedReason);

        world.Get<CraftControl>(craft).Throttle = 0;

        Assert.True(warp.Increase());

    }

    [Fact]
    public void ApplyingThrustMidWarpDropsToRealTime() {

        var world = new World();
        var craft = world.Create();

        world.Add(craft, new CraftControl());

        var warp = new TimeWarpController();

        warp.AddConstraint(new ThrustWarpConstraint(world));
        warp.SetLevel(6);

        world.Get<CraftControl>(craft).Translation = new Brutal.Numerics.float3(0, 0, 1);
        warp.Update();

        Assert.Equal(0, warp.Level);
        Assert.Equal(TimeWarpMode.Physics, warp.Mode);

    }

    [Fact]
    public void PhysicsTableIsSeparateFromRails() {

        var warp = new TimeWarpController(physicsRates: [1, 2, 4]);

        warp.SetLevel(5);
        warp.SelectMode(TimeWarpMode.Physics);

        Assert.Equal(1, warp.Rate);

        warp.Increase();

        Assert.Equal(2, warp.Rate);
        Assert.Equal(TimeWarpMode.Physics, warp.Mode);

    }

    [Fact]
    public void RejectsTablesThatDoNotStartAtRealTime() {

        Assert.Throws<ArgumentException>(() => new TimeWarpController([2, 4]));

    }

}
