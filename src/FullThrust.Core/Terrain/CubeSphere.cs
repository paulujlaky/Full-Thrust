using Brutal.Numerics;

namespace FullThrust.Core.Terrain;

// A quadtree node on one of the six cube faces; x and y index the 2^level grid across the face.
public readonly record struct TerrainNode(int Face, int Level, int X, int Y) {

    public const int MaxLevel = 24;

    public ulong Key => ((ulong)(uint)Face << 58) | ((ulong)(uint)Level << 52) | ((ulong)(uint)X << 26) | (uint)Y;

    public static TerrainNode FromKey(ulong key) => new((int)(key >> 58), (int)((key >> 52) & 0x3F), (int)((key >> 26) & 0x3FFFFFF), (int)(key & 0x3FFFFFF));

    public TerrainNode Child(int index) => new(Face, Level + 1, X * 2 + (index & 1), Y * 2 + (index >> 1));

    public TerrainNode Parent => new(Face, Level - 1, X >> 1, Y >> 1);

    // Face coordinates in [-1, 1].
    public double Size => 2.0 / (1 << Level);

    public double U0 => -1 + X * Size;

    public double V0 => -1 + Y * Size;

}

// Equal-angle cube-to-sphere mapping. Each face's U x V is its outward normal, so grid triangles wind counter-clockwise from outside.
public static class CubeSphere {

    private static readonly double3[] Normals = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];
    private static readonly double3[] UAxes = [new(0, 0, -1), new(0, 0, 1), new(1, 0, 0), new(1, 0, 0), new(1, 0, 0), new(-1, 0, 0)];
    private static readonly double3[] VAxes = [new(0, 1, 0), new(0, 1, 0), new(0, 0, -1), new(0, 0, 1), new(0, 1, 0), new(0, 1, 0)];

    public static double3 Direction(int face, double u, double v) {

        var cube = Normals[face] + UAxes[face] * Math.Tan(u * Math.PI / 4) + VAxes[face] * Math.Tan(v * Math.PI / 4);

        return double3.Normalize(cube);

    }

    public static double3 Center(in TerrainNode node) => Direction(node.Face, node.U0 + node.Size / 2, node.V0 + node.Size / 2);

    // Arc length of a node edge on a sphere of the given radius (equal-angle mapping keeps this nearly uniform).
    public static double EdgeLength(in TerrainNode node, double radius) => radius * Math.PI / 2 * node.Size / 2;

    // Smallest level whose vertices are no farther apart than spacing.
    public static int LevelForSpacing(double radius, int quadsPerEdge, double spacing) {

        var faceEdge = radius * Math.PI / 2;
        var level = (int)Math.Ceiling(Math.Log2(faceEdge / (quadsPerEdge * spacing)));

        return Math.Clamp(level, 0, TerrainNode.MaxLevel);

    }

}
