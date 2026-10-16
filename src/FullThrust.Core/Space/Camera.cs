using Brutal.Numerics;

namespace FullThrust.Core.Space;

public sealed class Camera {

    public double3 Position;
    public doubleQuat Rotation = doubleQuat.Identity;

    public float VerticalFov = MathF.PI / 3.0f;

    public float NearPlane = 0.1f;

    public double3 Forward => Rotation * new double3(0, 0, -1);

    public double3 Up => Rotation * double3.UnitY;

    public float4x4 ViewMatrix(FloatingOrigin origin) {

        return float4x4.CreateLookTo(origin.ToLocal(Position), SpaceMath.ToFloat(Forward), SpaceMath.ToFloat(Up));

    }

    public float4x4 ProjectionMatrix(float aspectRatio) => SpaceMath.PerspectiveReverseZ(VerticalFov, aspectRatio, NearPlane);

}
