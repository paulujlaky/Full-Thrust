using Brutal.Numerics;
using FullThrust.Core.Ecs;

namespace FullThrust.Core.Parts;

public enum EngineFeed {

    Turbopump,
    PressureFed,

}

public sealed class TankDefinition : IPartModuleDefinition {

    public ResourceSet Capacity;

    public ResourceSet Density;

    public required double Volume { get; init; }

    public required double Pressure { get; init; }

    public required double PressurantMass { get; init; }

    public void Attach(World world, Entity part) {

        var state = new TankState {

            Definition = this,
            Amount = Capacity,

            Pressure = Pressure,
            PressurantMass = PressurantMass,

        };

        state.UllageVolume = Volume - state.PropellantVolume;

        world.Add(part, state);

    }

}

public sealed class EngineDefinition : IPartModuleDefinition {

    public required int Fuel { get; init; }

    // Negative for monopropellant engines.
    public required int Oxidizer { get; init; }

    public required double MixtureRatio { get; init; }

    public required double VacuumThrust { get; init; }

    public required double VacuumIsp { get; init; }

    public required double SeaLevelIsp { get; init; }

    public required double MinThrottle { get; init; }

    public required double GimbalRange { get; init; }

    public required double GimbalRate { get; init; }

    public required EngineFeed Feed { get; init; }

    public required double SpoolUpTime { get; init; }

    public required double SpoolDownTime { get; init; }

    public required double MinInletPressure { get; init; }

    public required double FeedPressure { get; init; }

    public required int Ignitions { get; init; }

    public required bool RequiresUllage { get; init; }

    public double MaxMassFlow => VacuumThrust / (VacuumIsp * PhysicalConstants.StandardGravity);

    // Thrust lost to back-pressure on the nozzle exit: F = mdot * Isp_vac * g0 - p * A_exit.
    public double ExitArea => MaxMassFlow * (VacuumIsp - SeaLevelIsp) * PhysicalConstants.StandardGravity / PhysicalConstants.SeaLevelPressure;

    public double FuelFraction => Oxidizer < 0 ? 1.0 : 1.0 / (1.0 + MixtureRatio);

    public double OxidizerFraction => Oxidizer < 0 ? 0.0 : MixtureRatio / (1.0 + MixtureRatio);

    public double ThrustAt(double massFlow, double ambientPressure) {

        return Math.Max(0, massFlow * VacuumIsp * PhysicalConstants.StandardGravity - ambientPressure * ExitArea);

    }

    public void Attach(World world, Entity part) {

        world.Add(part, new EngineState {

            Definition = this,
            IgnitionsRemaining = Ignitions,
            ThrustDirection = double3.UnitY,

        });

    }

}

public sealed class RcsDefinition : IPartModuleDefinition {

    public required int Propellant { get; init; }

    public required double NozzleThrust { get; init; }

    public required double VacuumIsp { get; init; }

    public required double SeaLevelIsp { get; init; }

    public required double3[] Nozzles { get; init; }

    public double NozzleMassFlow => NozzleThrust / (VacuumIsp * PhysicalConstants.StandardGravity);

    public double NozzleExitArea => NozzleMassFlow * (VacuumIsp - SeaLevelIsp) * PhysicalConstants.StandardGravity / PhysicalConstants.SeaLevelPressure;

    public void Attach(World world, Entity part) => world.Add(part, new RcsState { Definition = this });

}

public sealed class DecouplerDefinition : IPartModuleDefinition {

    public required double EjectionImpulse { get; init; }

    public void Attach(World world, Entity part) => world.Add(part, new DecouplerState { Definition = this });

}

public sealed class CommandDefinition : IPartModuleDefinition {

    public required double ReactionWheelTorque { get; init; }

    public void Attach(World world, Entity part) => world.Add(part, new CommandPod { Definition = this });

}
