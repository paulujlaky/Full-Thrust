using Brutal.Numerics;
using FullThrust.Core.Atmosphere;
using FullThrust.Core.Craft;
using FullThrust.Core.Ecs;
using FullThrust.Core.Parts;

namespace FullThrust.Core.Tests;

// Drives EngineSystem.Step directly so feed, spool and ignition logic are tested without rigid-body dynamics.
public class EngineTests {

    private const double Dt = 0.02;

    private sealed class Rig : IDisposable {

        public readonly FlightHarness Harness = new();

        public readonly Entity Craft;
        public readonly Entity Engine;

        public FlightState Flight = new() { Mode = FlightMode.Physics, UllageSettle = 1 };

        public Rig(string rocket, string engine) {

            Craft = Harness.SpawnInOrbit(rocket, 300_000);
            Engine = Harness.FindPart<EngineState>(Craft, engine);

            Harness.Get<EngineState>(Engine).Armed = true;

        }

        public World World => Harness.World;

        public ref EngineState State => ref World.Get<EngineState>(Engine);

        public ref Part Part => ref World.Get<Part>(Engine);

        public void Run(float throttle, double seconds) {

            for (var t = 0.0; t < seconds - 1e-9; t += Dt) {

                EngineSystem.Step(World, ref State, in Part, ref Flight, throttle, true, Dt);
                TankPressureSystem.Pressurize(ref World.Get<TankState>(FirstTank()));

            }

        }

        public Entity FirstTank() {

            foreach (var row in World.Query<TankState, Part>()) {

                if (row.C2.Craft == Craft && row.C2.FeedGroup == Part.FeedGroup) {

                    return row.Entity;

                }

            }

            throw new InvalidOperationException("No tank in the engine's feed group.");

        }

        public void Dispose() => Harness.Dispose();

    }

    [Fact]
    public void TurbopumpSpoolsUpToFullThrust() {

        using var rig = new Rig("harrier", "engine-harrier-9");

        var definition = rig.State.Definition;

        rig.Run(1, definition.SpoolUpTime * 0.1);

        Assert.Equal(EngineStatus.Starting, rig.State.Status);
        Assert.True(rig.State.Pump < 0.5);

        rig.Run(1, definition.SpoolUpTime);

        Assert.Equal(EngineStatus.Running, rig.State.Status);
        Assert.Equal(definition.VacuumThrust, rig.State.Thrust, definition.VacuumThrust * 0.05);
        Assert.Equal(definition.VacuumIsp, rig.State.Isp, 1e-6);

    }

    [Fact]
    public void ThrustAndIspDropAtSeaLevel() {

        using var rig = new Rig("harrier", "engine-harrier-9");

        rig.Flight.Ambient = new AtmosphereSample(101325, 1.225, 288.15, 340);
        rig.Run(1, 6);

        var definition = rig.State.Definition;

        Assert.Equal(definition.SeaLevelIsp, rig.State.Isp, 0.5);
        Assert.Equal(definition.VacuumThrust * definition.SeaLevelIsp / definition.VacuumIsp, rig.State.Thrust, definition.VacuumThrust * 0.01);

    }

    [Fact]
    public void PropellantFlowsAtTheMixtureRatio() {

        using var rig = new Rig("harrier", "engine-harrier-9");

        var lox = rig.Harness.Parts.FindResource("LOX")!.Id;
        var rp1 = rig.Harness.Parts.FindResource("RP-1")!.Id;
        var tank = rig.FirstTank();

        var before = rig.World.Get<TankState>(tank);

        rig.Run(1, 5);

        var after = rig.World.Get<TankState>(tank);
        var oxidizer = before.Amount[lox] - after.Amount[lox];
        var fuel = before.Amount[rp1] - after.Amount[rp1];

        Assert.True(fuel > 0);
        Assert.Equal(rig.State.Definition.MixtureRatio, oxidizer / fuel, 1e-9);

    }

    [Fact]
    public void ThrottleIsLimitedToTheEngineRange() {

        using var rig = new Rig("harrier", "engine-harrier-9");

        var definition = rig.State.Definition;

        rig.Run(0.05f, 8);

        var expected = definition.MinThrottle + (1 - definition.MinThrottle) * 0.05;

        Assert.Equal(expected, rig.State.Pump, 0.01);
        Assert.Equal(definition.MaxMassFlow * expected, rig.State.MassFlow, definition.MaxMassFlow * 0.01);

    }

    [Fact]
    public void IgnitionsAreLimited() {

        using var rig = new Rig("pinnacle", "engine-ra7");

        rig.Run(1, 1);
        rig.Run(0, 1);

        Assert.Equal(EngineStatus.Off, rig.State.Status);

        rig.Run(1, 0.1);

        Assert.Equal(EngineStatus.IgnitionFailed, rig.State.Status);
        Assert.Equal(EngineFault.NoIgnitions, rig.State.Fault);

    }

    [Fact]
    public void UnsettledPropellantFailsIgnitionAndSpendsTheIgniter() {

        using var rig = new Rig("harrier", "engine-harrier-vac");

        var ignitions = rig.State.IgnitionsRemaining;

        rig.Flight.UllageSettle = 0;
        rig.Run(1, 0.1);

        Assert.Equal(EngineFault.Unsettled, rig.State.Fault);
        Assert.Equal(ignitions - 1, rig.State.IgnitionsRemaining);
        Assert.Equal(0, rig.State.Thrust);

    }

    [Fact]
    public void LowTankPressureCavitatesTheTurbopump() {

        using var rig = new Rig("harrier", "engine-harrier-9");

        rig.Run(1, 3);

        Assert.Equal(EngineStatus.Running, rig.State.Status);

        rig.World.Get<TankState>(rig.FirstTank()).Pressure = 50_000;
        rig.World.Get<TankState>(rig.FirstTank()).PressurantMass = 0;
        rig.Run(1, 0.1);

        Assert.Equal(EngineStatus.Flameout, rig.State.Status);
        Assert.Equal(EngineFault.Cavitation, rig.State.Fault);

    }

    [Fact]
    public void PressureFedThrustBlowsDownWithoutPressurant() {

        using var rig = new Rig("colossus", "engine-service");

        var tank = rig.FirstTank();

        rig.World.Get<TankState>(tank).PressurantMass = 0;
        rig.Run(1, 5);

        var early = rig.State.Thrust;
        var earlyPressure = rig.World.Get<TankState>(tank).Pressure;

        rig.Run(1, 60);

        Assert.True(rig.World.Get<TankState>(tank).Pressure < earlyPressure);
        Assert.True(rig.State.Thrust < early || rig.State.Fault == EngineFault.LowFeedPressure);

    }

    [Fact]
    public void RegulatedPressurantHoldsTankPressureUntilExhausted() {

        using var rig = new Rig("harrier", "engine-harrier-vac");

        var tank = rig.FirstTank();
        var regulated = rig.World.Get<TankState>(tank).Definition.Pressure;
        var helium = rig.World.Get<TankState>(tank).PressurantMass;

        rig.Run(1, 30);

        ref readonly var state = ref rig.World.Get<TankState>(tank);

        Assert.Equal(regulated, state.Pressure, 1e-6);
        Assert.True(state.PressurantMass < helium);

    }

    [Fact]
    public void EmptyTanksFlameTheEngineOut() {

        using var rig = new Rig("pinnacle", "engine-ra7");

        rig.Run(1, 2);

        ref var tank = ref rig.World.Get<TankState>(rig.FirstTank());

        for (var i = 0; i < ResourceSet.Capacity; i++) {

            tank.Amount[i] = Math.Min(tank.Amount[i], 1);

        }

        rig.Run(1, 1);

        Assert.Equal(EngineStatus.Flameout, rig.State.Status);
        Assert.Equal(EngineFault.Depleted, rig.State.Fault);

    }

    [Fact]
    public void GimbalTracksAttitudeCommandAtItsSlewRate() {

        using var rig = new Rig("harrier", "engine-harrier-9");

        var mass = rig.World.Get<CraftMass>(rig.Craft);
        var command = new AttitudeCommand { Rotation = new double3(1, 0, 0) };
        var definition = rig.State.Definition;

        EngineSystem.Steer(ref rig.State, in rig.Part, in command, in mass, Dt);

        Assert.Equal(definition.GimbalRate * Dt, rig.State.Gimbal.Length(), 1e-9);

        for (var i = 0; i < 200; i++) {

            EngineSystem.Steer(ref rig.State, in rig.Part, in command, in mass, Dt);

        }

        Assert.Equal(definition.GimbalRange, rig.State.Gimbal.Length(), 1e-9);

        // The deflected thrust must turn the craft about the commanded axis.
        var lever = rig.Part.LocalPosition - mass.CenterOfMass;
        var torque = double3.Cross(lever, rig.State.ThrustDirection);

        Assert.True(torque.X > 0);

    }

}
