using Brutal.Numerics;

namespace FullThrust.Core.Space;

public struct Transform {

    public double3 Position;
    public doubleQuat Rotation;

    public Transform(double3 position, doubleQuat rotation) {

        Position = position;
        Rotation = rotation;

    }

    public readonly double3 Forward => Rotation * new double3(0, 0, -1);

    public readonly double3 Up => Rotation * double3.UnitY;

    public readonly double3 Right => Rotation * double3.UnitX;

}

public struct Velocity {

    public double3 Linear;

    // World-space axis * radians per second.
    public double3 Angular;

}

public struct Name {

    public string Value;

    public Name(string value) {

        Value = value;

    }

}
