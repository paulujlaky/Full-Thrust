using System.Runtime.InteropServices;
using Brutal.Numerics;

namespace FullThrust.Game.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex {

    public float3 Position;
    public float3 Normal;

    public Vertex(float3 position, float3 normal) {

        Position = position;
        Normal = normal;

    }

}

public sealed class MeshData {

    public required Vertex[] Vertices;

    public required uint[] Indices;

    public static MeshData UnitBox() {

        var vertices = new Vertex[24];
        var indices = new uint[36];

        Span<float3> normals = [

            new(1, 0, 0), new(-1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, -1),

        ];

        for (var face = 0; face < 6; face++) {

            var n = normals[face];

            var u = MathF.Abs(n.Y) > 0.5f ? new float3(1, 0, 0) : new float3(0, 1, 0);
            var v = float3.Cross(n, u);

            var baseVertex = face * 4;

            vertices[baseVertex + 0] = new Vertex((n - u - v) * 0.5f, n);
            vertices[baseVertex + 1] = new Vertex((n + u - v) * 0.5f, n);
            vertices[baseVertex + 2] = new Vertex((n + u + v) * 0.5f, n);
            vertices[baseVertex + 3] = new Vertex((n - u + v) * 0.5f, n);

            var i = face * 6;

            indices[i + 0] = (uint)baseVertex;
            indices[i + 1] = (uint)(baseVertex + 1);
            indices[i + 2] = (uint)(baseVertex + 2);
            indices[i + 3] = (uint)baseVertex;
            indices[i + 4] = (uint)(baseVertex + 2);
            indices[i + 5] = (uint)(baseVertex + 3);

        }

        return new MeshData { Vertices = vertices, Indices = indices };

    }

    // Unit height along Y centred on the origin, bottom radius 0.5; a top radius of 0.5 gives a cylinder, 0 a cone.
    public static MeshData UnitFrustum(float topRadius, int segments) {

        const float BottomRadius = 0.5f;

        var hasTop = topRadius > 0;
        var capCount = hasTop ? 2 : 1;

        var vertices = new Vertex[(segments + 1) * 2 + capCount * (segments + 1)];
        var indices = new uint[segments * 6 + capCount * segments * 3];

        var vi = 0;
        var ii = 0;
        var slope = BottomRadius - topRadius;

        for (var s = 0; s <= segments; s++) {

            var theta = 2.0f * MathF.PI * s / segments;
            var cos = MathF.Cos(theta);
            var sin = -MathF.Sin(theta);
            var normal = float3.Normalize(new float3(cos, slope, sin));

            vertices[vi++] = new Vertex(new float3(cos * BottomRadius, -0.5f, sin * BottomRadius), normal);
            vertices[vi++] = new Vertex(new float3(cos * topRadius, 0.5f, sin * topRadius), normal);

        }

        for (var s = 0; s < segments; s++) {

            var a = (uint)(s * 2);

            indices[ii++] = a;
            indices[ii++] = a + 2;
            indices[ii++] = a + 1;
            indices[ii++] = a + 1;
            indices[ii++] = a + 2;
            indices[ii++] = a + 3;

        }

        for (var cap = 0; cap < capCount; cap++) {

            var y = cap == 0 ? -0.5f : 0.5f;
            var radius = cap == 0 ? BottomRadius : topRadius;
            var normal = new float3(0, cap == 0 ? -1 : 1, 0);
            var center = (uint)vi;

            vertices[vi++] = new Vertex(new float3(0, y, 0), normal);

            for (var s = 0; s < segments; s++) {

                var theta = 2.0f * MathF.PI * s / segments;

                vertices[vi++] = new Vertex(new float3(MathF.Cos(theta) * radius, y, -MathF.Sin(theta) * radius), normal);

            }

            for (var s = 0; s < segments; s++) {

                indices[ii++] = center;
                indices[ii++] = center + 1 + (uint)s;
                indices[ii++] = center + 1 + (uint)((s + 1) % segments);

            }

        }

        return new MeshData { Vertices = vertices[..vi], Indices = indices[..ii] };

    }

    public static MeshData UnitSphere(int rings, int segments) {

        var vertices = new Vertex[(rings + 1) * (segments + 1)];
        var indices = new uint[rings * segments * 6];

        var vi = 0;

        for (var r = 0; r <= rings; r++) {

            var phi = MathF.PI * r / rings;
            var y = MathF.Cos(phi);
            var radius = MathF.Sin(phi);

            for (var s = 0; s <= segments; s++) {

                var theta = 2.0f * MathF.PI * s / segments;
                var n = new float3(radius * MathF.Cos(theta), y, -radius * MathF.Sin(theta));

                vertices[vi++] = new Vertex(n, n);

            }

        }

        var ii = 0;
        var stride = segments + 1;

        for (var r = 0; r < rings; r++) {

            for (var s = 0; s < segments; s++) {

                var a = (uint)(r * stride + s);
                var b = (uint)((r + 1) * stride + s);

                indices[ii++] = a;
                indices[ii++] = b;
                indices[ii++] = a + 1;
                indices[ii++] = a + 1;
                indices[ii++] = b;
                indices[ii++] = b + 1;

            }

        }

        return new MeshData { Vertices = vertices, Indices = indices };

    }

}
