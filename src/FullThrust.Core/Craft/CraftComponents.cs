using Brutal.Numerics;
using FullThrust.Core.Atmosphere;
using FullThrust.Core.Ecs;
using FullThrust.Core.Physics;

namespace FullThrust.Core.Craft;

public struct Vessel {

    public Entity Root;

    public int PartCount;

    // Only craft with a command pod respond to pilot input; spent stages coast.
    public bool Controllable;

    public bool ShapeDirty;

    // Keeps freshly separated stages in physics so they drift apart before going back on rails.
    public double SeparationTimer;

    // Lowest point below the origin along body -Y, used for ground clearance.
    public double Bottom;

}

public struct CraftMass {

    public double Mass;

    public double3 CenterOfMass;

    public InertiaTensor Inertia;

    public readonly MassDistribution Distribution => new() { Mass = Mass, CenterOfMass = CenterOfMass, Inertia = Inertia };

}

// World-space force through the centre of mass and torque about it, cleared after every physics step.
public struct CraftForces {

    public double3 Force;
    public double3 Torque;

    public void AddAt(double3 force, double3 pointFromCenterOfMass) {

        Force += force;
        Torque += double3.Cross(pointFromCenterOfMass, force);

    }

}

public struct CraftStaging {

    public int NextStage;
    public int StageCount;

    public bool Requested;

}

public struct AttitudeCommand {

    // Body axes, each -1..1: rotation (pitch X, roll Y, yaw Z) and translation.
    public double3 Rotation;
    public double3 Translation;

    public bool Holding;
    public doubleQuat HoldTarget;

    // Peak control torque per body axis, gathered from the actuators during the previous step.
    public double3 Authority;
    public double3 AuthorityNext;

}

public enum FlightMode {

    Rails,
    Physics,

}

public struct FlightState {

    public FlightMode Mode;

    public int Body;

    public double Altitude;

    // Above the terrain (or the sea surface), rather than the reference radius.
    public double SurfaceAltitude;

    public AtmosphereSample Ambient;

    // Velocity relative to the co-rotating atmosphere.
    public double3 AirVelocity;

    public double Mach;
    public double DynamicPressure;

    public double Drag;

    // Engine and RCS thrust from the latest step, summed as magnitudes.
    public double Thrust;

    public double3 Gravity;

    public double3 CenterOfMassVelocity;
    public double3 ProperAcceleration;

    // 0 = propellant floating, 1 = settled at the tank outlets.
    public double UllageSettle;

    public bool Landed;

    public double RailsTimer;

}

public sealed class FlightFocus {

    public Entity Craft;

}
