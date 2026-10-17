using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Orbits;
using FullThrust.Core.Simulation;
using FullThrust.Game.Rendering;

namespace FullThrust.Game.Debug;

// Draws everything in the root inertial frame, so patch joins at SOI boundaries show up as continuous lines.
public sealed class OrbitDebugDraw {

    private static readonly float4[] PatchColors = [

        new(0.30f, 0.95f, 0.45f, 1),
        new(1.00f, 0.65f, 0.20f, 1),
        new(0.75f, 0.45f, 1.00f, 1),
        new(0.30f, 0.80f, 1.00f, 1),

    ];

    private readonly CelestialSystem _system;
    private readonly TrajectoryPredictor _predictor;
    private readonly DebugLines _lines;

    public OrbitDebugDraw(CelestialSystem system, TrajectoryPredictor predictor, DebugLines lines) {

        _system = system;
        _predictor = predictor;
        _lines = lines;

    }

    public bool Enabled { get; set; } = true;

    public bool DrawBodyOrbits { get; set; } = true;

    public bool DrawSpheresOfInfluence { get; set; } = true;

    public bool DrawCraftTrajectory { get; set; } = true;

    public float DegreesPerSegment { get; set; } = 1.0f;

    public int MaxPointsPerPatch { get; set; } = 4000;

    public void Draw(double now) {

        if (!Enabled) {

            return;

        }

        foreach (var body in _system.Bodies) {

            if (body.Parent is null) {

                continue;

            }

            var color = new float4(body.DebugColor * 0.8f, 0.6f);
            var state = _system.RelativeState(body, now);

            if (DrawBodyOrbits) {

                DrawConic(body.Parent.Index, state, now, now, now + body.OrbitalPeriod, body.OrbitGravitationalParameter, color);

            }

            if (DrawSpheresOfInfluence) {

                var normal = double3.Cross(state.Position, state.Velocity);

                _lines.Circle(_system.Position(body.Index), normal, body.SphereOfInfluence, new float4(color.XYZ, 0.35f));

            }

        }

        if (!DrawCraftTrajectory) {

            return;

        }

        var patches = _predictor.Patches;

        for (var i = 0; i < patches.Length; i++) {

            ref readonly var patch = ref patches[i];

            var start = Math.Max(now, patch.StartTime);
            var mu = _system[patch.Body].GravitationalParameter;

            DrawConic(patch.Body, patch.Epoch, patch.EpochTime, start, patch.EndTime, mu, PatchColors[i % PatchColors.Length]);

            if (patch.EndsInTransition && DrawSpheresOfInfluence) {

                var entering = _system[patch.NextBody].Parent?.Index == patch.Body;
                var sphere = entering ? _system[patch.NextBody] : _system[patch.Body];

                if (sphere.Parent is null) {

                    continue;

                }

                var center = _system.StateAt(sphere.Index, patch.EndTime).Position;
                var local = _system.RelativeState(sphere, patch.EndTime);

                // Ghost of the sphere where the crossing will happen.
                _lines.Circle(center, double3.Cross(local.Position, local.Velocity), sphere.SphereOfInfluence, new float4(1, 1, 1, 0.25f));

            }

        }

    }

    private void DrawConic(int body, in StateVector epoch, double epochTime, double from, double to, double mu, float4 color) {

        if (!(to > from)) {

            return;

        }

        var step = DegreesPerSegment * Math.PI / 180.0;
        var t = from;

        _lines.BeginStrip();

        for (var i = 0; i < MaxPointsPerPatch; i++) {

            var relative = _system.Propagator.Propagate(epoch, mu, t - epochTime);
            var frame = _system.StateAt(body, t).Position;

            _lines.StripPoint(frame + relative.Position, color);

            if (t >= to) {

                break;

            }

            // Advance by a fixed swept angle so fast periapsis passes stay smooth.
            var dt = step * relative.Position.Length() / Math.Max(relative.Velocity.Length(), 1e-3);

            t = Math.Min(to, t + dt);

        }

    }

}
