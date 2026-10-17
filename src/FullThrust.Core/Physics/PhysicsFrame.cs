using Brutal.Numerics;

namespace FullThrust.Core.Physics;

// Jolt is single precision, so physics runs in a frame that coasts with the focus craft and re-anchors to it.
public sealed class PhysicsFrame {

    public double3 Origin { get; private set; }

    public double3 Velocity { get; private set; }

    public double RecenterDistance { get; set; } = 200.0;

    public double RecenterSpeed { get; set; } = 20.0;

    public long ShiftCount { get; private set; }

    public double3 ToLocalPosition(double3 world) => world - Origin;

    public double3 ToLocalVelocity(double3 world) => world - Velocity;

    public double3 ToWorldPosition(double3 local) => Origin + local;

    public double3 ToWorldVelocity(double3 local) => Velocity + local;

    public void Advance(double deltaTime) => Origin += Velocity * deltaTime;

    // Only valid while no bodies exist, since it moves the frame without telling them.
    public void Reset(double3 origin, double3 velocity) {

        Origin = origin;
        Velocity = velocity;

    }

    public bool Recenter(double3 focusPosition, double3 focusVelocity, IRigidBodyWorld bodies) {

        var localPosition = focusPosition - Origin;
        var localVelocity = focusVelocity - Velocity;

        var shiftPosition = localPosition.LengthSquared() > RecenterDistance * RecenterDistance;
        var shiftVelocity = localVelocity.LengthSquared() > RecenterSpeed * RecenterSpeed;

        if (!shiftPosition && !shiftVelocity) {

            return false;

        }

        var positionDelta = shiftPosition ? localPosition : double3.Zero;
        var velocityDelta = shiftVelocity ? localVelocity : double3.Zero;

        Origin += positionDelta;
        Velocity += velocityDelta;
        ShiftCount++;

        bodies.ShiftFrame(positionDelta, velocityDelta);

        return true;

    }

}
