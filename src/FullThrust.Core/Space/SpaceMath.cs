using System.Runtime.CompilerServices;
using Brutal.Numerics;

namespace FullThrust.Core.Space;

public static class SpaceMath {

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float3 ToFloat(double3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double3 ToDouble(float3 v) => new(v.X, v.Y, v.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static floatQuat ToFloat(doubleQuat q) => new((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);

    public static doubleQuat Integrate(doubleQuat rotation, double3 angularVelocity, double deltaTime) {

        var speed = angularVelocity.Length();

        if (speed < 1e-12) {

            return rotation;

        }

        var step = doubleQuat.CreateFromAxisAngle(angularVelocity / speed, speed * deltaTime);

        return doubleQuat.Normalize(step * rotation);

    }

    // Right-handed, Vulkan clip space (Y down, depth 0..1), reverse-Z with an infinite far plane.
    public static float4x4 PerspectiveReverseZ(float verticalFov, float aspectRatio, float nearPlane) {

        var f = 1.0f / MathF.Tan(verticalFov * 0.5f);

        float4x4 result;

        result.X = new float4(f / aspectRatio, 0, 0, 0);
        result.Y = new float4(0, -f, 0, 0);
        result.Z = new float4(0, 0, 0, -1);
        result.W = new float4(0, 0, nearPlane, 0);

        return result;

    }

}
