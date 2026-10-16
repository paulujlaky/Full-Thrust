using Brutal.Numerics;

namespace FullThrust.Core.Craft;

public struct CraftControl {

    // Each axis is -1..1 in craft body space.
    public float Pitch;
    public float Yaw;
    public float Roll;

    public float3 Translation;

    public float Throttle;

}

public struct ControllableCraft {

    public double Mass;

    // Placeholder actuation until Sprint 3 engines/RCS replace it.
    public double MaxThrust;
    public double MaxAngularAcceleration;

    public double AngularDamping;

}
