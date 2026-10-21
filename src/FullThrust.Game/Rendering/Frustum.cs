using Brutal.Numerics;

namespace FullThrust.Game.Rendering;

// Clip-space planes for v * M with reverse-Z (depth 1 near, 0 far); planes with no normal always pass.
public readonly struct Frustum {

    private readonly float4 _left;
    private readonly float4 _right;
    private readonly float4 _bottom;
    private readonly float4 _top;
    private readonly float4 _near;
    private readonly float4 _far;

    private Frustum(float4 left, float4 right, float4 bottom, float4 top, float4 near, float4 far) {

        _left = left;
        _right = right;
        _bottom = bottom;
        _top = top;
        _near = near;
        _far = far;

    }

    // Shadow casters between the sun and a cascade must not be culled by its near plane.
    public static Frustum FromMatrix(in float4x4 m, bool includeNear = true) {

        var c0 = new float4(m.X.X, m.Y.X, m.Z.X, m.W.X);
        var c1 = new float4(m.X.Y, m.Y.Y, m.Z.Y, m.W.Y);
        var c2 = new float4(m.X.Z, m.Y.Z, m.Z.Z, m.W.Z);
        var c3 = new float4(m.X.W, m.Y.W, m.Z.W, m.W.W);

        return new Frustum(

            Normalize(c3 + c0),
            Normalize(c3 - c0),
            Normalize(c3 + c1),
            Normalize(c3 - c1),
            includeNear ? Normalize(c3 - c2) : default,
            Normalize(c2)

        );

    }

    public bool Intersects(float3 center, float radius) {

        return Inside(_left, center, radius) && Inside(_right, center, radius) && Inside(_bottom, center, radius) && Inside(_top, center, radius) && Inside(_near, center, radius) && Inside(_far, center, radius);

    }

    private static bool Inside(float4 p, float3 c, float radius) => p.X * c.X + p.Y * c.Y + p.Z * c.Z + p.W >= -radius;

    private static float4 Normalize(float4 p) {

        var length = new float3(p.X, p.Y, p.Z).Length();

        return length > 1e-12f ? p / length : default;

    }

}
