using Brutal.Numerics;
using FullThrust.Core.Space;

namespace FullThrust.Game.Rendering.Volumetrics;

// A drifting ball of smoke beside the camera target, for checking the volumetric pipeline in isolation.
public sealed class DebugVolumeSource : IVolumeSource {

    private readonly Func<double3> _anchor;
    private readonly FloatingOrigin _origin;
    private readonly Func<double> _seconds;

    public DebugVolumeSource(Func<double3> anchor, Func<double> seconds, FloatingOrigin origin) {

        _anchor = anchor;
        _seconds = seconds;
        _origin = origin;

    }

    public bool Visible { get; set; } = true;

    public float Radius { get; set; } = 18.0f;

    public double3 Offset { get; set; } = new(25, 10, 0);

    public void Collect(VolumeList volumes) {

        var position = _origin.ToLocal(_anchor() + Offset);
        var drift = (float)(_seconds() * 0.01 % 1.0);

        volumes.Add(new VolumePrimitive {

            Kind = new int4((int)VolumeKind.NoiseSphere, 48, 6, 0),
            Position = new float4(position, Radius),
            Axis = new float4(0, 1, 0, 0),
            Medium = new float4(0.9f, 0.9f, 0.9f, 0.5f),
            Emission = new float4(0, 0, 0, 0.6f),
            Params0 = new float4(Radius * 0.7f, drift, 0, 0),

        });

    }

}
