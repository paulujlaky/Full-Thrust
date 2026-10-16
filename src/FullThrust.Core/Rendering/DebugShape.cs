using Brutal.Numerics;

namespace FullThrust.Core.Rendering;

public enum ShapeKind {

    Box,
    Sphere,

}

public struct DebugShape {

    public ShapeKind Kind;

    public float3 Scale;

    public float4 Color;

}
