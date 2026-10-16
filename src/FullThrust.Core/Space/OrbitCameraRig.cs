using Brutal.Numerics;

namespace FullThrust.Core.Space;

public sealed class OrbitCameraRig {

    public double Yaw;
    public double Pitch = 0.3;

    public double Distance = 25.0;

    public double MinDistance = 2.0;
    public double MaxDistance = 5.0e7;

    public void Orbit(double deltaYaw, double deltaPitch) {

        Yaw += deltaYaw;
        Pitch = Math.Clamp(Pitch + deltaPitch, -1.55, 1.55);

    }

    public void Zoom(double factor) => Distance = Math.Clamp(Distance * factor, MinDistance, MaxDistance);

    // Up follows the local vertical so the horizon stays level around a planet.
    public void Apply(Camera camera, double3 target, double3 up) {

        up = double3.NormalizeOrZero(up);

        if (up == double3.Zero) {

            up = double3.UnitY;

        }

        var reference = Math.Abs(up.Y) < 0.99 ? double3.UnitY : double3.UnitX;
        var east = double3.Normalize(double3.Cross(reference, up));
        var north = double3.Cross(up, east);

        var horizontal = east * Math.Cos(Yaw) + north * Math.Sin(Yaw);
        var offset = (horizontal * Math.Cos(Pitch) + up * Math.Sin(Pitch)) * Distance;

        camera.Position = target + offset;
        camera.Rotation = LookRotation(double3.Normalize(-offset), up);

    }

    public static doubleQuat LookRotation(double3 forward, double3 up) {

        var back = -forward;
        var right = double3.Normalize(double3.Cross(up, back));
        var trueUp = double3.Cross(back, right);

        double4x4 basis;

        basis.X = new double4(right, 0);
        basis.Y = new double4(trueUp, 0);
        basis.Z = new double4(back, 0);
        basis.W = new double4(0, 0, 0, 1);

        return doubleQuat.Normalize(doubleQuat.CreateFromRotationMatrix(basis));

    }

}
