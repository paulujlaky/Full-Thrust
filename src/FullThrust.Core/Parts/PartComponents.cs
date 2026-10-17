using Brutal.Numerics;
using FullThrust.Core.Ecs;

namespace FullThrust.Core.Parts;

public struct Part {

    public Entity Craft;
    public Entity Parent;

    public PartDefinition Definition;

    // Pose in craft space; the craft origin is the root part's centre and +Y is the nose.
    public double3 LocalPosition;
    public doubleQuat LocalRotation;

    public bool Radial;

    public int Stage;

    // Parts share a feed group when propellant can flow between them without crossing a decoupler.
    public int FeedGroup;

    public PartAero Aero;

}

public struct PartAero {

    // Areas left uncovered by neighbouring stack parts, facing nose-first and tail-first flow.
    public double TopArea;
    public double BottomArea;

    public double SideArea;

}

public struct TankState {

    public TankDefinition Definition;

    public ResourceSet Amount;

    public double Pressure;
    public double PressurantMass;

    public double UllageVolume;

    public readonly double PropellantMass => Amount.Sum();

    public readonly double PropellantVolume {

        get {

            var volume = 0.0;
            var density = Definition.Density;

            for (var i = 0; i < ResourceSet.Capacity; i++) {

                if (Amount[i] > 0) {

                    volume += Amount[i] / density[i];

                }

            }

            return volume;

        }

    }

    public readonly double FillFraction => Definition.Volume > 0 ? Math.Clamp(PropellantVolume / Definition.Volume, 0, 1) : 0;

}

public enum EngineStatus {

    Off,
    Starting,
    Running,
    Flameout,
    IgnitionFailed,

}

public enum EngineFault {

    None,
    NoIgnitions,
    NoPropellant,
    Unsettled,
    LowInletPressure,
    Cavitation,
    Depleted,
    LowFeedPressure,

}

public struct EngineState {

    public EngineDefinition Definition;

    // Staged engines respond to the throttle; unstaged ones stay cold.
    public bool Armed;

    public EngineStatus Status;
    public EngineFault Fault;

    public int IgnitionsRemaining;

    // Ignition triggers on the throttle's rising edge so a failed start never retries every step.
    public bool CommandHigh;

    // Turbopump speed (or valve opening when pressure-fed) as a fraction of full flow.
    public double Pump;

    // Craft-space deflection perpendicular to the thrust axis; magnitude is the angle in radians.
    public double3 Gimbal;

    public double Thrust;
    public double MassFlow;
    public double Isp;

    public double3 ThrustDirection;

    public readonly bool IsBurning => Status is EngineStatus.Starting or EngineStatus.Running || Pump > 0.01;

}

public struct RcsState {

    public RcsDefinition Definition;

    public double Thrust;
    public double MassFlow;

}

public struct DecouplerState {

    public DecouplerDefinition Definition;

    public bool Fired;

}

public struct CommandPod {

    public CommandDefinition Definition;

}
