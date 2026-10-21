using Brutal.ImGuiApi;
using Brutal.Numerics;
using FullThrust.Core.Craft;
using FullThrust.Game.Controls;
using FullThrust.Game.Scene;

namespace FullThrust.Game.Debug;

public sealed class ScenarioPanel : IDebugPanel {

    private readonly Action<Scenario, RocketPreset> _reset;

    private readonly DemoScene _scene;
    private readonly OrbitDebugDraw _orbits;
    private readonly CraftDebugDraw _craftDraw;
    private readonly CameraController _camera;

    private readonly Action _capture;

    private int _rocket;

    public ScenarioPanel(Action<Scenario, RocketPreset> reset, DemoScene scene, OrbitDebugDraw orbits, CraftDebugDraw craftDraw, CameraController camera, Action capture) {

        _reset = reset;
        _scene = scene;
        _orbits = orbits;
        _craftDraw = craftDraw;
        _camera = camera;
        _capture = capture;

        _rocket = Math.Max(0, scene.Rockets.IndexOf(scene.Rocket));

    }

    public string Title => "Launch";

    public bool Visible { get; set; } = true;

    public void Draw() {

        ImGui.SetNextWindowPos(new float2(730, 30), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Launch"u8)) {

            DrawLaunch();

            ImGui.SeparatorText("Camera focus (Tab)"u8);

            for (var i = 0; i < _camera.TargetCount; i++) {

                if (i > 0) {

                    ImGui.SameLine();

                }

                if (ImGui.RadioButton(_camera.TargetLabel(i), _camera.TargetIndex == i)) {

                    _camera.Focus(i);

                }

            }

            ImGui.SeparatorText("Orbit lines"u8);

            var enabled = _orbits.Enabled;

            if (ImGui.Checkbox("Draw orbits"u8, ref enabled)) {

                _orbits.Enabled = enabled;

            }

            var bodies = _orbits.DrawBodyOrbits;

            if (ImGui.Checkbox("Body orbits"u8, ref bodies)) {

                _orbits.DrawBodyOrbits = bodies;

            }

            var spheres = _orbits.DrawSpheresOfInfluence;

            if (ImGui.Checkbox("Spheres of influence"u8, ref spheres)) {

                _orbits.DrawSpheresOfInfluence = spheres;

            }

            var trajectory = _orbits.DrawCraftTrajectory;

            if (ImGui.Checkbox("Craft trajectory"u8, ref trajectory)) {

                _orbits.DrawCraftTrajectory = trajectory;

            }

            var step = _orbits.DegreesPerSegment;

            if (ImGui.SliderFloat("Degrees per segment"u8, ref step, 0.1f, 5f, "%.1f"u8)) {

                _orbits.DegreesPerSegment = step;

            }

            var exhaust = _craftDraw.Enabled;

            if (ImGui.Checkbox("Airflow lines"u8, ref exhaust)) {

                _craftDraw.Enabled = exhaust;

            }

            if (ImGui.Button("Capture screenshot (F12)"u8)) {

                _capture();

            }

        }

        ImGui.End();

    }

    private void DrawLaunch() {

        var rockets = _scene.Rockets.Rockets;

        ImGui.SeparatorText("Rocket"u8);

        for (var i = 0; i < rockets.Count; i++) {

            var rocket = rockets[i];

            if (ImGui.RadioButton(rocket.Name, _rocket == i)) {

                _rocket = i;

            }

            ImGui.SameLine();
            ImGui.TextDisabled($"{rocket.TotalMass / 1000:F0} t, {rocket.Height:F0} m, {rocket.StageCount} stages");

        }

        var selected = rockets[_rocket];

        ImGui.TextWrapped(selected.Description);

        ImGui.SeparatorText("Start"u8);

        if (ImGui.Button("Launch pad"u8)) {

            _reset(Scenario.Launchpad, selected);

        }

        ImGui.SameLine();

        if (ImGui.Button("Low Earth orbit"u8)) {

            _reset(Scenario.LowOrbit, selected);

        }

        ImGui.SameLine();

        if (ImGui.Button("Lunar transfer"u8)) {

            _reset(Scenario.LunarTransfer, selected);

        }

        ImGui.TextDisabled($"Current: {_scene.Rocket.Name}, scenario {ScenarioName(_scene.Scenario)}");

    }

    private static string ScenarioName(Scenario scenario) => scenario switch {

        Scenario.Launchpad => "launch pad",
        Scenario.LowOrbit => "low orbit",
        _ => "lunar transfer",

    };

}
