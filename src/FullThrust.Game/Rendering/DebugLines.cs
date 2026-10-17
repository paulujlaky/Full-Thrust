using System.Runtime.InteropServices;
using Brutal.Numerics;
using FullThrust.Core.Space;

namespace FullThrust.Game.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct LineVertex {

    public float3 Position;
    public float4 Color;

    public LineVertex(float3 position, float4 color) {

        Position = position;
        Color = color;

    }

}

// Fixed-capacity line list rebuilt every frame; overflow is dropped rather than reallocated.
public sealed class DebugLines {

    private readonly LineVertex[] _vertices;

    private readonly FloatingOrigin _origin;

    private int _count;

    private bool _hasPrevious;
    private LineVertex _previous;

    public DebugLines(FloatingOrigin origin, int capacity = 65536) {

        _origin = origin;
        _vertices = new LineVertex[capacity];

    }

    public int Count => _count;

    public int Capacity => _vertices.Length;

    public long Dropped { get; private set; }

    public ReadOnlySpan<LineVertex> Vertices => new(_vertices, 0, _count);

    public void Clear() {

        _count = 0;
        _hasPrevious = false;

    }

    public void Line(double3 from, double3 to, float4 color) {

        Push(new LineVertex(_origin.ToLocal(from), color));
        Push(new LineVertex(_origin.ToLocal(to), color));

    }

    public void BeginStrip() => _hasPrevious = false;

    public void StripPoint(double3 world, float4 color) {

        var vertex = new LineVertex(_origin.ToLocal(world), color);

        if (_hasPrevious) {

            Push(_previous);
            Push(vertex);

        }

        _previous = vertex;
        _hasPrevious = true;

    }

    public void Circle(double3 center, double3 normal, double radius, float4 color, int segments = 128) {

        normal = double3.Normalize(normal);

        var reference = Math.Abs(normal.Y) < 0.99 ? double3.UnitY : double3.UnitX;
        var u = double3.Normalize(double3.Cross(reference, normal));
        var v = double3.Cross(normal, u);

        BeginStrip();

        for (var i = 0; i <= segments; i++) {

            var angle = 2 * Math.PI * i / segments;

            StripPoint(center + (u * Math.Cos(angle) + v * Math.Sin(angle)) * radius, color);

        }

    }

    private void Push(in LineVertex vertex) {

        if (_count >= _vertices.Length) {

            Dropped++;

            return;

        }

        _vertices[_count++] = vertex;

    }

}
