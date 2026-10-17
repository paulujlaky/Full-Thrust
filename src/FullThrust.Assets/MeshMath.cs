using Brutal.Numerics;

namespace FullThrust.Assets;

public static class MeshMath {

    // Area-weighted smooth normals over the indexed triangles.
    public static void ComputeNormals(Span<MeshVertex> vertices, ReadOnlySpan<uint> indices) {

        for (var i = 0; i < indices.Length; i++) {

            vertices[(int)indices[i]].Normal = float3.Zero;

        }

        for (var i = 0; i + 2 < indices.Length; i += 3) {

            ref var a = ref vertices[(int)indices[i]];
            ref var b = ref vertices[(int)indices[i + 1]];
            ref var c = ref vertices[(int)indices[i + 2]];

            var normal = float3.Cross(b.Position - a.Position, c.Position - a.Position);

            a.Normal += normal;
            b.Normal += normal;
            c.Normal += normal;

        }

        for (var i = 0; i < indices.Length; i++) {

            ref var vertex = ref vertices[(int)indices[i]];

            vertex.Normal = float3.NormalizeOrZero(vertex.Normal);

        }

    }

    // Per-vertex tangent frames from UV gradients (Lengyel), orthogonalised against the normal.
    public static void ComputeTangents(Span<MeshVertex> vertices, ReadOnlySpan<uint> indices) {

        var count = vertices.Length;
        var tangents = new float3[count];
        var bitangents = new float3[count];

        for (var i = 0; i + 2 < indices.Length; i += 3) {

            int i0 = (int)indices[i], i1 = (int)indices[i + 1], i2 = (int)indices[i + 2];

            var e1 = vertices[i1].Position - vertices[i0].Position;
            var e2 = vertices[i2].Position - vertices[i0].Position;
            var d1 = vertices[i1].Uv - vertices[i0].Uv;
            var d2 = vertices[i2].Uv - vertices[i0].Uv;

            var determinant = d1.X * d2.Y - d2.X * d1.Y;

            if (MathF.Abs(determinant) < 1e-12f) {

                continue;

            }

            var r = 1.0f / determinant;
            var t = (e1 * d2.Y - e2 * d1.Y) * r;
            var b = (e2 * d1.X - e1 * d2.X) * r;

            tangents[i0] += t; tangents[i1] += t; tangents[i2] += t;
            bitangents[i0] += b; bitangents[i1] += b; bitangents[i2] += b;

        }

        for (var i = 0; i < indices.Length; i++) {

            var v = (int)indices[i];
            ref var vertex = ref vertices[v];

            var n = vertex.Normal;
            var t = float3.NormalizeOrZero(tangents[v] - n * float3.Dot(n, tangents[v]));

            if (t == float3.Zero) {

                t = float3.Normalize(float3.Cross(n, MathF.Abs(n.Y) < 0.99f ? float3.UnitY : float3.UnitX));

            }

            var sign = float3.Dot(float3.Cross(n, t), bitangents[v]) < 0 ? -1.0f : 1.0f;

            vertex.Tangent = new float4(t, sign);

        }

    }

}
