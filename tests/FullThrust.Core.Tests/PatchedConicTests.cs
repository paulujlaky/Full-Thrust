using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Tests;

public class PatchedConicTests {

    private const double ParkingRadius = 6_571_000;

    [Fact]
    public void LowOrbitNeverSamplesForTransitions() {

        var system = TestData.EarthMoon();
        var solver = new PatchedConicSolver(system);
        var leo = OrbitScenarios.Circular(system.Root.GravitationalParameter, ParkingRadius);

        var transition = solver.FindNext(0, leo, 0, 0, 30 * 86400);

        Assert.False(transition.Found);
        Assert.Equal(0, solver.SamplesLastSearch);

    }

    [Fact]
    public void LunarTransferEntersMoonSphereBeforeApoapsis() {

        var system = TestData.EarthMoon();
        var moon = system.Find("Moon")!;
        var solver = new PatchedConicSolver(system);
        var transfer = OrbitScenarios.LunarTransfer(system, moon, ParkingRadius, 0);

        var transition = solver.FindNext(0, transfer, 0, 0, 10 * 86400);

        Assert.True(transition.Found);
        Assert.Equal(moon.Index, transition.ToBody);
        Assert.InRange(transition.Time / 86400, 2.0, 5.5);

        var craft = system.Propagator.Propagate(transfer, system.Root.GravitationalParameter, transition.Time);
        var moonPosition = system.RelativeState(moon, transition.Time).Position;

        Assert.Equal(moon.SphereOfInfluence, double3.Distance(craft.Position, moonPosition), 2_000.0);

    }

    [Fact]
    public void CoarseSearchWindowsFindTheSameCrossing() {

        var system = TestData.EarthMoon();
        var moon = system.Find("Moon")!;
        var transfer = OrbitScenarios.LunarTransfer(system, moon, ParkingRadius, 0);

        var single = new PatchedConicSolver(system).FindNext(0, transfer, 0, 0, 10 * 86400);

        var chunked = new PatchedConicSolver(system);
        var found = default(SoiTransition);

        for (var t = 0.0; t < 10 * 86400 && !found.Found; t += 6000) {

            found = chunked.FindNext(0, transfer, 0, t, t + 6000);

        }

        Assert.True(found.Found);
        Assert.Equal(single.Time, found.Time, 0.01);

    }

    [Fact]
    public void RailsWarpCarriesCraftIntoAndBackOutOfMoonSphere() {

        var system = TestData.EarthMoon();
        var moon = system.Find("Moon")!;
        var events = new OrbitEvents();

        var world = new World();
        var scheduler = new SystemScheduler();

        scheduler
            .Add(SystemPhase.LateUpdate, new CelestialBodySystem(system))
            .Add(SystemPhase.LateUpdate, new OrbitPropagationSystem(system, new PatchedConicSolver(system), events));

        var craft = world.Create();

        world.Add(craft, new Transform(double3.Zero, doubleQuat.Identity));
        world.Add(craft, new Velocity());
        world.Add(craft, OrbitState.Create(0, OrbitScenarios.LunarTransfer(system, moon, ParkingRadius, 0), 0));

        var warp = new TimeWarpController();
        var clock = new SimulationClock(warp);

        warp.SetLevel(7);

        var lastDistanceToMoon = double.MaxValue;

        for (var frame = 0; frame < 60 * 20 && events.TransitionCount < 2; frame++) {

            clock.Advance(1.0 / 60.0);
            scheduler.Run(SystemPhase.LateUpdate, world, clock.FrameTime);

            var orbit = world.Get<OrbitState>(craft);

            if (orbit.Body == moon.Index) {

                lastDistanceToMoon = Math.Min(lastDistanceToMoon, orbit.Current.Position.Length());

            }

        }

        Assert.Equal(2, events.TransitionCount);
        Assert.Equal(moon.Index, events.Recent(1).ToBody);
        Assert.Equal(0, events.Recent(0).ToBody);
        Assert.True(lastDistanceToMoon < moon.SphereOfInfluence);

        var position = world.Get<Transform>(craft).Position;
        var expected = system.StateAt(0, clock.UniversalTime).Position + world.Get<OrbitState>(craft).Current.Position;

        Assert.True(double3.Distance(position, expected) < 1e-3);

    }

    [Fact]
    public void PredictorChainsPatchesThroughTheMoon() {

        var system = TestData.EarthMoon();
        var moon = system.Find("Moon")!;
        var predictor = new TrajectoryPredictor(system, new PatchedConicSolver(system));

        var orbit = OrbitState.Create(0, OrbitScenarios.LunarTransfer(system, moon, ParkingRadius, 0), 0);

        predictor.Update(orbit, 0);

        Assert.True(predictor.Count >= 3);
        Assert.Equal(0, predictor.Patches[0].Body);
        Assert.Equal(moon.Index, predictor.Patches[1].Body);
        Assert.Equal(0, predictor.Patches[2].Body);
        Assert.Equal(predictor.Patches[0].EndTime, predictor.TimeToNextTransition(0), 1e-9);

    }

}
