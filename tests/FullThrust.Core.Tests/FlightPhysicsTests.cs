using Brutal.Numerics;
using FullThrust.Core.Craft;
using FullThrust.Core.Orbits;
using FullThrust.Core.Parts;
using FullThrust.Core.Physics;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Tests;

// End-to-end flight through Jolt: the same systems, order and frame handling the game uses.
public class FlightPhysicsTests {

    [Fact]
    public void RocketRestsOnThePadInPhysics() {

        using var harness = new FlightHarness();

        var craft = harness.SpawnOnPad("pinnacle");

        harness.Run(1);

        var altitude = harness.Get<FlightState>(craft).Altitude;

        harness.Run(3);

        ref readonly var flight = ref harness.Get<FlightState>(craft);

        Assert.Equal(FlightMode.Physics, flight.Mode);
        Assert.True(harness.World.Has<PhysicsBody>(craft));
        Assert.True(flight.Landed);
        Assert.Equal(altitude, flight.Altitude, 0.05);
        Assert.True(flight.AirVelocity.Length() < 0.05, $"air {flight.AirVelocity.Length():F4} m/s");

        // Standing on the ground the accelerometer reads one g upward, which keeps propellant settled.
        Assert.Equal(harness.Earth.SurfaceGravity, flight.ProperAcceleration.Length(), 0.1);
        Assert.Equal(1, flight.UllageSettle, 1e-9);

    }

    [Fact]
    public void LaunchClimbsAndBurnsPropellantAtTheRatedFlow() {

        using var harness = new FlightHarness();

        var craft = harness.SpawnOnPad("pinnacle");
        var engine = harness.FindPart<EngineState>(craft, "engine-ra7");
        var definition = harness.Get<EngineState>(engine).Definition;

        harness.Run(0.5);

        var startMass = harness.Get<CraftMass>(craft).Mass;
        var startAltitude = harness.Get<FlightState>(craft).Altitude;

        harness.Get<CraftControl>(craft).Throttle = 1;
        harness.Get<CraftControl>(craft).Sas = true;
        harness.Stage(craft);
        harness.Run(15);

        ref readonly var flight = ref harness.Get<FlightState>(craft);
        ref readonly var state = ref harness.Get<EngineState>(engine);

        Assert.Equal(EngineStatus.Running, state.Status);
        Assert.False(flight.Landed);
        Assert.True(flight.Altitude - startAltitude > 250, $"climbed {flight.Altitude - startAltitude:F1} m");
        Assert.InRange(state.Isp, definition.SeaLevelIsp, definition.VacuumIsp);

        // Less than the full 15 s at rated flow because of turbopump spool-up.
        var burned = startMass - harness.Get<CraftMass>(craft).Mass;

        Assert.InRange(burned, definition.MaxMassFlow * 13, definition.MaxMassFlow * 15);

        // SAS keeps a symmetric stack pointing straight up.
        var up = double3.Normalize(harness.Get<OrbitState>(craft).Current.Position);
        var nose = harness.Get<Transform>(craft).Rotation * double3.UnitY;

        Assert.True(double3.Dot(up, nose) > 0.999);

    }

    [Fact]
    public void CoastingInPhysicsMatchesTheConicThenReturnsToRails() {

        using var harness = new FlightHarness();

        var craft = harness.SpawnInOrbit("pinnacle", 300_000);
        var initial = harness.Get<OrbitState>(craft).Epoch;

        harness.Get<Vessel>(craft).SeparationTimer = 10;
        harness.Run(10);

        Assert.Equal(FlightMode.Physics, harness.Get<FlightState>(craft).Mode);

        var expected = harness.System.Propagator.Propagate(initial, harness.Earth.GravitationalParameter, harness.Clock.UniversalTime);
        var actual = harness.Get<Transform>(craft).Position - harness.System.StateAt(0, harness.Clock.UniversalTime).Position;

        Assert.True(double3.Distance(expected.Position, actual) < 1.0, $"drift {double3.Distance(expected.Position, actual):F3} m");

        harness.Run(2);

        Assert.Equal(FlightMode.Rails, harness.Get<FlightState>(craft).Mode);
        Assert.False(harness.World.Has<PhysicsBody>(craft));
        Assert.Equal(0, harness.Bodies.BodyCount);

        var energyBefore = Energy(initial, harness.Earth.GravitationalParameter);
        var energyAfter = Energy(harness.Get<OrbitState>(craft).Epoch, harness.Earth.GravitationalParameter);

        Assert.Equal(energyBefore, energyAfter, Math.Abs(energyBefore) * 1e-6);

    }

    [Fact]
    public void ThrustChangesVelocityByForceOverMass() {

        using var harness = new FlightHarness();

        var craft = harness.SpawnInOrbit("harrier", 300_000);
        var start = harness.Get<OrbitState>(craft);
        var mu = harness.Earth.GravitationalParameter;

        harness.Get<CraftControl>(craft).Throttle = 1;
        harness.Get<CraftControl>(craft).Sas = true;
        harness.Stage(craft);

        var expected = 0.0;

        for (var i = 0; i < 250; i++) {

            harness.Run(FlightHarness.Step);

            // Mass is computed before the actuators run, so it is the mass Jolt integrated with this step.
            expected += harness.Get<FlightState>(craft).Thrust / harness.Get<CraftMass>(craft).Mass * FlightHarness.Step;

        }

        var actual = harness.Get<OrbitState>(craft).Current;
        var drift = harness.System.Propagator.Propagate(start.Epoch, mu, harness.Clock.UniversalTime - start.EpochTime);
        var gained = actual.Velocity - drift.Velocity;

        Assert.True(expected > 50);
        Assert.Equal(expected, gained.Length(), expected * 0.01);

        // The burn went along the nose, which started out prograde.
        Assert.True(double3.Dot(double3.Normalize(gained), double3.Normalize(start.Current.Velocity)) > 0.99);

    }

    [Fact]
    public void DragOpposesAirflowInTheAtmosphere() {

        using var harness = new FlightHarness();

        var earth = harness.Earth;
        var radius = earth.Radius + 8_000;
        var position = new double3(radius, 0, 0);
        var surface = double3.Cross(earth.AngularVelocity, position);
        var state = new StateVector(position, surface + new double3(0, 0, -400));

        var preset = harness.Rocket("pinnacle");

        harness.Focus.Craft = CraftPlacement.InOrbit(harness.System, earth, state, 0).Spawn(harness.World, preset);

        var craft = harness.Focus.Craft;

        harness.Run(0.2);

        var before = harness.Get<FlightState>(craft).AirVelocity;

        harness.Run(2);

        ref readonly var flight = ref harness.Get<FlightState>(craft);

        Assert.True(flight.Drag > 1_000);
        Assert.True(flight.Ambient.Density > 0.4);
        Assert.True(Math.Abs(flight.AirVelocity.Z) < Math.Abs(before.Z));

    }

    [Fact]
    public void RcsTranslatesAndRotatesTheCraft() {

        using var harness = new FlightHarness();

        var craft = harness.SpawnInOrbit("pinnacle", 300_000);
        var hydrazine = harness.Parts.FindResource("Hydrazine")!.Id;
        var pod = harness.FindPart<TankState>(craft, "pod-pinnacle");
        var propellant = harness.Get<TankState>(pod).Amount[hydrazine];

        ref var control = ref harness.Get<CraftControl>(craft);

        control.Rcs = true;
        control.Translation = new float3(0, 1, 0);

        var coast = harness.Get<OrbitState>(craft);

        harness.Run(2);

        var mu = harness.Earth.GravitationalParameter;
        var drift = harness.System.Propagator.Propagate(coast.Epoch, mu, harness.Clock.UniversalTime - coast.EpochTime);
        var gained = harness.Get<OrbitState>(craft).Current.Velocity - drift.Velocity;
        var nose = harness.Get<Transform>(craft).Rotation * double3.UnitY;

        Assert.True(double3.Dot(gained, nose) > 0.05);
        Assert.True(harness.Get<TankState>(pod).Amount[hydrazine] < propellant);

        harness.Get<CraftControl>(craft).Translation = default;
        harness.Get<CraftControl>(craft).Pitch = 1;
        harness.Run(1);

        var spin = harness.Get<Velocity>(craft).Angular.Length();

        Assert.True(spin > 0.01);

        harness.Get<CraftControl>(craft).Pitch = 0;
        harness.Get<CraftControl>(craft).Sas = true;
        harness.Run(15);

        Assert.True(harness.Get<Velocity>(craft).Angular.Length() < spin * 0.1);

    }

    [Fact]
    public void SecondStageSeparatesAndIgnites() {

        using var harness = new FlightHarness();

        var craft = harness.SpawnInOrbit("harrier", 300_000);

        harness.Get<CraftControl>(craft).Throttle = 1;
        harness.Stage(craft);
        harness.Run(5);
        harness.Stage(craft);
        harness.Run(4);

        var boosterEngine = harness.FindPart<EngineState>("engine-harrier-9");
        var booster = harness.Get<Part>(boosterEngine).Craft;
        var upperEngine = harness.Get<EngineState>(harness.FindPart<EngineState>(craft, "engine-harrier-vac"));

        Assert.NotEqual(craft, booster);
        Assert.Equal(EngineStatus.Running, upperEngine.Status);
        Assert.True(harness.Get<EngineState>(boosterEngine).Thrust == 0);

        var separation = harness.ComPosition(craft) - harness.ComPosition(booster);
        var nose = harness.Get<Transform>(craft).Rotation * double3.UnitY;

        Assert.True(double3.Dot(separation, nose) > 30);

    }

    [Fact]
    public void RailsWarpIsBlockedOnThePad() {

        using var harness = new FlightHarness();

        harness.SpawnOnPad("pinnacle");
        harness.Run(0.5);

        Assert.False(harness.Warp.SetLevel(3));
        Assert.NotNull(harness.Warp.BlockedReason);

    }

    private static double Energy(in StateVector state, double mu) => state.Velocity.LengthSquared() / 2 - mu / state.Position.Length();

}
