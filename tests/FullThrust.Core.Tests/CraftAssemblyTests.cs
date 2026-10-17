using FullThrust.Core.Craft;
using FullThrust.Core.Parts;

namespace FullThrust.Core.Tests;

public class CraftAssemblyTests {

    [Fact]
    public void SpawnedMassMatchesThePreset() {

        using var harness = new FlightHarness();

        var craft = harness.SpawnOnPad("harrier");

        Assert.Equal(harness.Rocket("harrier").TotalMass, harness.Get<CraftMass>(craft).Mass, 1e-6);

    }

    [Fact]
    public void DrainingPropellantMovesTheCentreOfMassAndShrinksInertia() {

        using var harness = new FlightHarness();

        var craft = harness.SpawnOnPad("harrier");
        var before = CraftAssembly.ComputeMass(harness.World, craft);

        var tank = harness.FindPart<TankState>(craft, "tank-harrier-1");

        ref var state = ref harness.Get<TankState>(tank);
        ref readonly var part = ref harness.Get<Part>(tank);

        for (var i = 0; i < ResourceSet.Capacity; i++) {

            state.Amount[i] *= 0.5;

        }

        // Settled propellant sits in the aft half of a half-empty tank.
        var length = part.Definition.Length;
        var fill = state.FillFraction;

        Assert.Equal(part.LocalPosition.Y - length / 2 + length * fill / 2, CraftAssembly.PropellantCenter(in part, in state).Y, 1e-9);

        var half = CraftAssembly.ComputeMass(harness.World, craft);

        for (var i = 0; i < ResourceSet.Capacity; i++) {

            state.Amount[i] = 0;

        }

        var empty = CraftAssembly.ComputeMass(harness.World, craft);

        Assert.True(half.Mass < before.Mass && empty.Mass < half.Mass);

        // The booster is at the bottom, so emptying it moves the centre of mass towards the nose.
        Assert.True(empty.CenterOfMass.Y > before.CenterOfMass.Y + 1);
        Assert.True(half.Inertia.Yy < before.Inertia.Yy && empty.Inertia.Yy < half.Inertia.Yy);
        Assert.True(empty.Inertia.Xx < before.Inertia.Xx);

    }

    [Fact]
    public void InertiaIncludesParallelAxisTerms() {

        using var harness = new FlightHarness();

        var craft = harness.SpawnOnPad("pinnacle");
        var mass = harness.Get<CraftMass>(craft);
        var rocket = harness.Rocket("pinnacle");

        // A stack is far longer than it is wide, so pitch inertia must dwarf roll inertia.
        Assert.True(mass.Inertia.Xx > 20 * mass.Inertia.Yy);
        Assert.Equal(mass.Inertia.Xx, mass.Inertia.Zz, mass.Inertia.Xx * 1e-9);
        Assert.True(mass.Inertia.Xx > mass.Mass * rocket.Height * rocket.Height / 40);

    }

    [Fact]
    public void FeedGroupsStopAtDecouplers() {

        using var harness = new FlightHarness();

        var craft = harness.SpawnOnPad("harrier");

        var booster = harness.Get<Part>(harness.FindPart<TankState>(craft, "tank-harrier-1")).FeedGroup;
        var upper = harness.Get<Part>(harness.FindPart<TankState>(craft, "tank-harrier-2")).FeedGroup;
        var pod = harness.Get<Part>(harness.FindPart<TankState>(craft, "pod-concord")).FeedGroup;

        Assert.Equal(booster, harness.Get<Part>(harness.FindPart<EngineState>(craft, "engine-harrier-9")).FeedGroup);
        Assert.Equal(upper, harness.Get<Part>(harness.FindPart<EngineState>(craft, "engine-harrier-vac")).FeedGroup);
        Assert.NotEqual(booster, upper);
        Assert.NotEqual(upper, pod);

    }

    [Fact]
    public void StagingArmsEnginesInSequence() {

        using var harness = new FlightHarness();

        var craft = harness.SpawnOnPad("harrier");

        var booster = harness.FindPart<EngineState>(craft, "engine-harrier-9");
        var upper = harness.FindPart<EngineState>(craft, "engine-harrier-vac");

        harness.Stage(craft);

        Assert.True(harness.Get<EngineState>(booster).Armed);
        Assert.False(harness.Get<EngineState>(upper).Armed);
        Assert.Equal(1, harness.Get<CraftStaging>(craft).NextStage);

    }

    [Fact]
    public void DecouplersSplitTheCraftBelowThem() {

        using var harness = new FlightHarness();

        var craft = harness.SpawnOnPad("harrier");
        var partCount = harness.Get<Vessel>(craft).PartCount;

        harness.Stage(craft);
        harness.Stage(craft);

        var boosterEngine = harness.FindPart<EngineState>("engine-harrier-9");
        var booster = harness.Get<Part>(boosterEngine).Craft;
        var decoupler = harness.Get<Vessel>(booster).Root;

        Assert.NotEqual(craft, booster);
        Assert.Equal(partCount, harness.Get<Vessel>(craft).PartCount + harness.Get<Vessel>(booster).PartCount);
        Assert.True(harness.Get<Vessel>(craft).Controllable);
        Assert.False(harness.Get<Vessel>(booster).Controllable);
        Assert.True(harness.Get<DecouplerState>(decoupler).Fired);
        Assert.Equal(1, harness.Get<Part>(decoupler).Stage);
        Assert.True(harness.Get<EngineState>(harness.FindPart<EngineState>(craft, "engine-harrier-vac")).Armed);
        Assert.Equal(2, harness.Get<CraftStaging>(craft).NextStage);

        // The upper stage's engine bell is now open to the airflow from behind.
        var vacuumEngine = harness.Get<Part>(harness.FindPart<EngineState>(craft, "engine-harrier-vac"));

        Assert.Equal(vacuumEngine.Definition.FrontalArea, vacuumEngine.Aero.BottomArea, 1e-9);

    }

    [Fact]
    public void StagingPastTheLastStageDoesNothing() {

        using var harness = new FlightHarness();

        var craft = harness.SpawnOnPad("pinnacle");

        for (var i = 0; i < 4; i++) {

            harness.Stage(craft);

        }

        Assert.Equal(2, harness.Get<CraftStaging>(craft).NextStage);
        Assert.Equal(2, harness.Flight.Staging.StageCount);

    }

}
