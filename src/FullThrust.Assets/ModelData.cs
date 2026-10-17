using System.Runtime.InteropServices;
using Brutal.Numerics;

namespace FullThrust.Assets;

[StructLayout(LayoutKind.Sequential)]
public struct MeshVertex {

    public float3 Position;
    public float3 Normal;
    public float2 Uv;

    // xyz tangent, w bitangent sign (glTF convention).
    public float4 Tangent;

    public MeshVertex(float3 position, float3 normal, float2 uv) {

        Position = position;
        Normal = normal;
        Uv = uv;
        Tangent = new float4(1, 0, 0, 1);

    }

}

public enum AlphaMode {

    Opaque,
    Mask,
    Blend,

}

public sealed class MaterialData {

    public string Name = "";

    public float4 BaseColor = new(1, 1, 1, 1);

    public float Metallic = 1;
    public float Roughness = 1;

    public float3 Emissive;

    public float NormalScale = 1;
    public float OcclusionStrength = 1;

    // Indices into ModelData.Textures, or -1.
    public int BaseColorTexture = -1;
    public int MetallicRoughnessTexture = -1;
    public int NormalTexture = -1;
    public int OcclusionTexture = -1;
    public int EmissiveTexture = -1;

    public AlphaMode AlphaMode;
    public float AlphaCutoff = 0.5f;

    public bool DoubleSided;

}

// Decoded RGBA8 pixels; Srgb marks colour data (base colour, emissive).
public sealed class TextureData {

    public string Name = "";

    public int Width;
    public int Height;

    public required byte[] Pixels;

    public bool Srgb;

    // Encoded source kept for re-export without a lossy round trip.
    public byte[]? Png;

}

// A run of triangles in the model's shared index buffer drawn with one material.
public readonly record struct PrimitiveData(int FirstIndex, int IndexCount, int Material);

// A glTF mesh: a contiguous range of primitives.
public readonly record struct MeshRange(int FirstPrimitive, int PrimitiveCount, string Name);

// One node that draws a mesh, with its transform flattened into model space.
public readonly record struct MeshInstance(int Mesh, float4x4 Transform);

public sealed class ModelData {

    public string Name = "";

    public MeshVertex[] Vertices = [];
    public uint[] Indices = [];

    public PrimitiveData[] Primitives = [];
    public MeshRange[] Meshes = [];
    public MeshInstance[] Instances = [];

    public MaterialData[] Materials = [];
    public TextureData[] Textures = [];

    public float3 BoundsMin;
    public float3 BoundsMax;

    // Recomputes model-space bounds over every instanced vertex.
    public void ComputeBounds() {

        var min = new float3(float.MaxValue);
        var max = new float3(float.MinValue);

        foreach (var instance in Instances) {

            var mesh = Meshes[instance.Mesh];

            for (var p = mesh.FirstPrimitive; p < mesh.FirstPrimitive + mesh.PrimitiveCount; p++) {

                var primitive = Primitives[p];

                for (var i = primitive.FirstIndex; i < primitive.FirstIndex + primitive.IndexCount; i++) {

                    var position = float3.Transform(Vertices[Indices[i]].Position, instance.Transform);

                    min = float3.Min(min, position);
                    max = float3.Max(max, position);

                }

            }

        }

        BoundsMin = min;
        BoundsMax = max;

    }

    public int TriangleCount {

        get {

            var count = 0;

            foreach (var instance in Instances) {

                var mesh = Meshes[instance.Mesh];

                for (var p = mesh.FirstPrimitive; p < mesh.FirstPrimitive + mesh.PrimitiveCount; p++) {

                    count += Primitives[p].IndexCount / 3;

                }

            }

            return count;

        }

    }

}
