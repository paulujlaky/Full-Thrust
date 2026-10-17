using Brutal.Numerics;

namespace FullThrust.Core.Physics;

public enum ShapePrimitive {

    Cylinder,
    Box,

}

// Collision primitive in body space; cylinders run along Y with Size = (radius, halfHeight, radius).
public readonly struct ShapeElement {

    public readonly ShapePrimitive Primitive;

    public readonly double3 Position;
    public readonly doubleQuat Rotation;

    public readonly double3 Size;

    public ShapeElement(ShapePrimitive primitive, double3 position, doubleQuat rotation, double3 size) {

        Primitive = primitive;
        Position = position;
        Rotation = rotation;
        Size = size;

    }

    public static ShapeElement Cylinder(double3 position, double radius, double halfHeight) {

        return new ShapeElement(ShapePrimitive.Cylinder, position, doubleQuat.Identity, new double3(radius, halfHeight, radius));

    }

}

// A square grid of heights in body space: sample (i, j) sits at Origin + (i * Spacing, height, j * Spacing), row-major in j.
public readonly ref struct HeightGrid {

    public readonly ReadOnlySpan<float> Heights;

    public readonly int SampleCount;

    public readonly double Spacing;

    public readonly double3 Origin;

    public HeightGrid(ReadOnlySpan<float> heights, int sampleCount, double spacing, double3 origin) {

        Heights = heights;
        SampleCount = sampleCount;
        Spacing = spacing;
        Origin = origin;

    }

}

public struct MassDistribution {

    public double Mass;

    // Body space, relative to the body origin.
    public double3 CenterOfMass;

    // About the centre of mass, in body axes.
    public InertiaTensor Inertia;

}

// Positions and velocities here are in the physics frame (see PhysicsFrame), not world space.
public struct RigidBodyState {

    // Body origin, which is the craft origin rather than the centre of mass.
    public double3 Position;
    public doubleQuat Rotation;

    // Velocity of the centre of mass.
    public double3 LinearVelocity;
    public double3 AngularVelocity;

    public double3 CenterOfMass;

}

public interface IRigidBodyWorld : IDisposable {

    int BodyCount { get; }

    int CreateDynamic(ReadOnlySpan<ShapeElement> shape, in MassDistribution mass, in RigidBodyState state);

    int CreateKinematic(ReadOnlySpan<ShapeElement> shape, double3 position, doubleQuat rotation);

    // Kinematic terrain, moved with MoveKinematic so contacts see the surface velocity as the planet turns.
    int CreateKinematicHeightField(in HeightGrid shape, double3 position, doubleQuat rotation);

    void SetHeightField(int body, in HeightGrid shape);

    void Destroy(int body);

    void SetShape(int body, ReadOnlySpan<ShapeElement> shape, double3 centerOfMass);

    void SetMass(int body, in MassDistribution mass);

    RigidBodyState GetState(int body);

    void SetState(int body, in RigidBodyState state);

    void MoveKinematic(int body, double3 position, doubleQuat rotation, double deltaTime);

    // Force through the centre of mass plus a torque about it.
    void AddForceAndTorque(int body, double3 force, double3 torque);

    // Re-expresses every body after the frame origin or frame velocity moves.
    void ShiftFrame(double3 positionDelta, double3 velocityDelta);

    void Step(double deltaTime);

}

// Marks an entity whose motion is owned by the rigid-body world rather than by rails.
public struct PhysicsBody {

    public int Handle;

}
