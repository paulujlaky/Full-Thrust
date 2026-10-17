using Brutal.Numerics;

namespace FullThrust.Core.Craft;

public struct CraftControl {

    // Each axis is -1..1 in craft body space.
    public float Pitch;
    public float Yaw;
    public float Roll;

    public float3 Translation;

    public float Throttle;

    public bool Sas;
    public bool Rcs;

    public readonly bool HasRotationInput => Pitch != 0 || Yaw != 0 || Roll != 0;

    public readonly bool HasTranslationInput => Translation.X != 0 || Translation.Y != 0 || Translation.Z != 0;

}
