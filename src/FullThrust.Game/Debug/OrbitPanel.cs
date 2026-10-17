using Brutal.ImGuiApi;
using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Simulation;
using FullThrust.Game.Scene;

namespace FullThrust.Game.Debug;

public sealed class OrbitPanel : IDebugPanel {

    private const double Rad = 180.0 / Math.PI;

    private readonly World _world;
    private readonly CelestialSystem _system;
    private readonly SimulationClock _clock;

    private readonly DemoScene _scene;
    private readonly TrajectoryPredictor _predictor;
    private readonly OrbitEvents _events;

    public OrbitPanel(World world, CelestialSystem system, SimulationClock clock, DemoScene scene, TrajectoryPredictor predictor, OrbitEvents events) {

        _world = world;
        _system = system;
        _clock = clock;
        _scene = scene;
        _predictor = predictor;
        _events = events;

    }

    public string Title => "Orbit";

    public bool Visible { get; set; } = true;

    public void Draw() {

        ImGui.SetNextWindowPos(new float2(1180, 30), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Orbit"u8)) {

            ImGui.End();

            return;

        }

        var craft = _scene.Craft;

        if (_world.IsAlive(craft)) {

            DrawCraftOrbit(_world.Get<OrbitState>(craft));

        }

        DrawPrediction();
        DrawEvents();
        DrawBodies();

        ImGui.End();

    }

    private void DrawCraftOrbit(in OrbitState orbit) {

        var body = _system[orbit.Body];
        var mu = body.GravitationalParameter;
        var elements = OrbitalElements.FromState(orbit.Current, mu);

        var radius = orbit.Current.Position.Length();

        ImGui.SeparatorText("Craft orbit"u8);
        ImGui.Text($"Reference body {body.Name}  (scenario {_scene.Scenario})");
        ImGui.Text($"Altitude {(radius - body.Radius) / 1000:F3} km  speed {orbit.Current.Velocity.Length():F2} m/s");
        ImGui.Text($"Periapsis {(elements.Periapsis - body.Radius) / 1000:F1} km");

        if (elements.IsClosed) {

            var (d, h, m, s) = TimeFormat.Split(elements.Period(mu));

            ImGui.Text($"Apoapsis  {(elements.Apoapsis - body.Radius) / 1000:F1} km");
            ImGui.Text($"Period    {d}d {h:00}:{m:00}:{s:00}");

        } else {

            ImGui.Text("Apoapsis  escape trajectory"u8);

        }

        ImGui.Text($"e {elements.Eccentricity:F5}  i {elements.Inclination * Rad:F3} deg");
        ImGui.Text($"LAN {elements.LongitudeOfAscendingNode * Rad:F2}  AoP {elements.ArgumentOfPeriapsis * Rad:F2}  nu {elements.TrueAnomaly * Rad:F2}");

    }

    private void DrawPrediction() {

        ImGui.SeparatorText("Patched conics"u8);

        var patches = _predictor.Patches;
        var now = _clock.UniversalTime;

        for (var i = 0; i < patches.Length; i++) {

            ref readonly var patch = ref patches[i];

            var (d, h, m, s) = TimeFormat.Split(patch.EndTime - now);
            var name = _system[patch.Body].Name;

            if (patch.EndsInTransition) {

                ImGui.Text($"{i}: {name} -> {_system[patch.NextBody].Name} in {d}d {h:00}:{m:00}:{s:00}");

            } else {

                ImGui.Text($"{i}: {name} (no transition within horizon)");

            }

        }

    }

    private void DrawEvents() {

        ImGui.SeparatorText("SOI transitions"u8);
        ImGui.Text($"Total {_events.TransitionCount}");

        for (var i = 0; i < _events.RecentCount && i < 6; i++) {

            var e = _events.Recent(i);
            var (d, h, m, s) = TimeFormat.Split(e.Time);

            ImGui.Text($"UT {d}d {h:00}:{m:00}:{s:00}  {_system[e.FromBody].Name} -> {_system[e.ToBody].Name}");

        }

    }

    private void DrawBodies() {

        if (!ImGui.CollapsingHeader("Bodies"u8)) {

            return;

        }

        foreach (var body in _system.Bodies) {

            ImGui.Text($"{body.Name}: R {body.Radius / 1000:F0} km  GM {body.GravitationalParameter:E4}");

            if (body.Parent is not null) {

                var distance = (_system.Position(body.Index) - _system.Position(body.Parent.Index)).Length();

                ImGui.Text($"   SOI {body.SphereOfInfluence / 1000:F0} km  period {body.OrbitalPeriod / 86400:F3} d  distance {distance / 1000:F0} km");

            }

            if (body.Atmosphere is { } atmosphere) {

                ImGui.Text($"   atmosphere {atmosphere.Height / 1000:F0} km  {atmosphere.SurfacePressure:F0} Pa  {atmosphere.Layers.Count} layers");

            }

        }

    }

}
