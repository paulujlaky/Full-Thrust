using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Space;

namespace FullThrust.Game.Rendering.Volumetrics;

// One cumulus layer per body with cloud data, turning with the planet and drifting on the wind.
public sealed class CloudSource : IVolumeSource, IFrameUniformsWriter {

    // Beyond this many radii a planet's clouds are too small to march.
    private const double MaxDistanceRadii = 40;

    private readonly CelestialSystem _system;
    private readonly FloatingOrigin _origin;

    private double3 _camera;
    private double _time;

    private VolumePrimitive _nearest;
    private double _nearestDistance = double.MaxValue;

    public CloudSource(CelestialSystem system, FloatingOrigin origin) {

        _system = system;
        _origin = origin;

    }

    public bool Visible { get; set; } = true;

    public float CoverageScale { get; set; } = 1.0f;

    // Shadows on the ground come from the layer of the body nearest the camera.
    public void Fill(ref FrameUniforms uniforms, bool enabled) {

        if (!enabled || !Visible || _nearestDistance == double.MaxValue) {

            uniforms.CloudShell = default;

            return;

        }

        uniforms.CloudShell = _nearest.Params0;
        uniforms.CloudWeather = _nearest.Params1;
        uniforms.CloudRotation = _nearest.Axis;

    }

    public void Update(double3 camera, double universalTime) {

        _camera = camera;
        _time = universalTime;

    }

    public void Collect(VolumeList volumes) {

        _nearestDistance = double.MaxValue;

        foreach (var body in _system.Bodies) {

            if (body.Clouds is not { } clouds) {

                continue;

            }

            var center = _system.StateAt(body.Index, _time).Position;

            if ((_camera - center).Length() > body.Radius * MaxDistanceRadii) {

                continue;

            }

            var inner = (float)(body.Radius + clouds.BaseAltitude);
            var outer = (float)(body.Radius + clouds.BaseAltitude + clouds.Thickness);

            // World to body-fixed, so the weather map turns with the ground below it.
            var turn = body.RotationAt(_time);
            var rotation = new doubleQuat(-turn.X, -turn.Y, -turn.Z, turn.W);

            var drift = clouds.WindSpeed * _time;

            var primitive = new VolumePrimitive {

                Kind = new int4((int)VolumeKind.CloudLayer, 96, 5, 0),
                Position = new float4(_origin.ToLocal(center), outer),
                Axis = new float4((float)rotation.X, (float)rotation.Y, (float)rotation.Z, (float)rotation.W),
                Medium = new float4(1, 1, 1, (float)clouds.Extinction),
                Emission = new float4(0, 0, 0, 0.8f),
                Params0 = new float4(_origin.ToLocal(center), inner),
                Params1 = new float4(outer, (float)Math.Clamp(clouds.Coverage * CoverageScale, 0, 1), (float)(1 / clouds.WeatherScale), (float)(drift / clouds.WeatherScale % 1.0)),
                Params2 = new float4((float)(drift / clouds.ShapeScale % 1.0), (float)(drift * 2 / clouds.DetailScale % 1.0), (float)(1 / clouds.ShapeScale), (float)(1 / clouds.DetailScale)),

            };

            volumes.Add(primitive);

            var distance = (_camera - center).Length() / body.Radius;

            if (distance < _nearestDistance) {

                _nearest = primitive;
                _nearestDistance = distance;

            }

        }

    }

}
