using Brutal.Numerics;

namespace FullThrust.Core.Physics;

public struct InertiaTensor {

    public double Xx;
    public double Yy;
    public double Zz;

    public double Xy;
    public double Xz;
    public double Yz;

    // Axis along Y, matching how parts stack.
    public static InertiaTensor SolidCylinder(double mass, double radius, double height) {

        var transverse = mass * (3 * radius * radius + height * height) / 12;

        return new InertiaTensor { Xx = transverse, Yy = 0.5 * mass * radius * radius, Zz = transverse };

    }

    public void Add(in InertiaTensor other) {

        Xx += other.Xx;
        Yy += other.Yy;
        Zz += other.Zz;
        Xy += other.Xy;
        Xz += other.Xz;
        Yz += other.Yz;

    }

    // Parallel-axis term for a mass whose own centre sits `offset` from the reference point.
    public void AddOffset(double mass, double3 offset) {

        Xx += mass * (offset.Y * offset.Y + offset.Z * offset.Z);
        Yy += mass * (offset.X * offset.X + offset.Z * offset.Z);
        Zz += mass * (offset.X * offset.X + offset.Y * offset.Y);
        Xy -= mass * offset.X * offset.Y;
        Xz -= mass * offset.X * offset.Z;
        Yz -= mass * offset.Y * offset.Z;

    }

    public readonly double3 Multiply(double3 v) {

        return new double3(Xx * v.X + Xy * v.Y + Xz * v.Z, Xy * v.X + Yy * v.Y + Yz * v.Z, Xz * v.X + Yz * v.Y + Zz * v.Z);

    }

    public readonly double3 Diagonal => new(Xx, Yy, Zz);

}
