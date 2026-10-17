using Brutal.Numerics;

namespace FullThrust.Core.Terrain;

// Body-fixed bounding sphere of a built chunk.
public readonly record struct TerrainBounds(double3 Center, double Radius);

// What the selector needs from whoever builds and stores chunk meshes.
public interface ITerrainChunkSource {

    // False until the chunk has been built.
    bool TryGetBounds(in TerrainNode node, out TerrainBounds bounds);

    // Asks for a chunk to be built; returns false when the build queue is full.
    bool Request(in TerrainNode node);

}

// Splits nodes within SplitDistance edge lengths of the camera, but only once all four children exist, so there are no holes.
public sealed class TerrainQuadtree {

    private readonly ITerrain _terrain;
    private readonly double _radius;

    private readonly TerrainNode[] _selected;

    private int _requestBudget;

    public TerrainQuadtree(ITerrain terrain, double radius, int quadsPerEdge, int capacity = 4096, double finestSpacing = 1.5) {

        _terrain = terrain;
        _radius = radius;
        _selected = new TerrainNode[capacity];

        MaxLevel = CubeSphere.LevelForSpacing(radius, quadsPerEdge, finestSpacing);

    }

    public int MaxLevel { get; set; }

    public double SplitDistance { get; set; } = 2.2;

    public int MaxRequestsPerUpdate { get; set; } = 48;

    public ReadOnlySpan<TerrainNode> Selected => _selected.AsSpan(0, Count);

    public int Count { get; private set; }

    public int Culled { get; private set; }

    public int DeepestLevel { get; private set; }

    // Camera in the body-fixed frame, metres from the body centre.
    public void Update(double3 camera, ITerrainChunkSource source) {

        Count = 0;
        Culled = 0;
        DeepestLevel = 0;
        _requestBudget = MaxRequestsPerUpdate;

        for (var face = 0; face < 6; face++) {

            Visit(new TerrainNode(face, 0, 0, 0), camera, source);

        }

    }

    private void Visit(in TerrainNode node, double3 camera, ITerrainChunkSource source) {

        if (!source.TryGetBounds(node, out var bounds)) {

            if (_requestBudget > 0 && source.Request(node)) {

                _requestBudget--;

            }

            return;

        }

        var edge = CubeSphere.EdgeLength(node, _radius);
        var direction = CubeSphere.Center(node);

        if (BelowHorizon(direction, edge, camera)) {

            Culled++;

            return;

        }

        var distance = Math.Max(0, (camera - bounds.Center).Length() - bounds.Radius);

        if (node.Level < MaxLevel && distance < SplitDistance * edge && ChildrenReady(node, source)) {

            for (var i = 0; i < 4; i++) {

                Visit(node.Child(i), camera, source);

            }

            return;

        }

        if (Count < _selected.Length) {

            _selected[Count++] = node;
            DeepestLevel = Math.Max(DeepestLevel, node.Level);

        }

    }

    // Requests any missing children and reports whether all four can be drawn.
    private bool ChildrenReady(in TerrainNode node, ITerrainChunkSource source) {

        var ready = true;

        for (var i = 0; i < 4; i++) {

            var child = node.Child(i);

            if (source.TryGetBounds(child, out _)) {

                continue;

            }

            ready = false;

            if (_requestBudget > 0 && source.Request(child)) {

                _requestBudget--;

            }

        }

        return ready;

    }

    // A point at radius r clears a sphere of radius s seen from distance d within acos(s / d) + acos(s / r).
    private bool BelowHorizon(double3 direction, double edge, double3 camera) {

        var d = camera.Length();
        var sphere = _radius + Math.Max(_terrain.MinHeight, -0.05 * _radius);

        if (d <= sphere) {

            return false;

        }

        var top = _radius + _terrain.MaxHeight;
        var nodeAngle = edge / _radius * 0.75;
        var limit = Math.Acos(sphere / d) + Math.Acos(Math.Min(1, sphere / top)) + nodeAngle;

        if (limit >= Math.PI) {

            return false;

        }

        return double3.Dot(direction, camera / d) < Math.Cos(limit);

    }

}
