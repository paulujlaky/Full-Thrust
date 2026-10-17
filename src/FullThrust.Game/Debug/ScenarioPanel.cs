using Brutal.ImGuiApi;
using Brutal.Numerics;
using FullThrust.Game.Controls;
using FullThrust.Game.Scene;

namespace FullThrust.Game.Debug;

public sealed class ScenarioPanel : IDebugPanel {

    private readonly Action<Scenario> _reset;

    private readonly OrbitDebugDraw _orbits;
    private readonly CameraController _camera;

    private readonly Action _capture;

    public ScenarioPanel(Action<Scenario> reset, OrbitDebugDraw orbits, CameraController camera, Action capture) {

        _reset = reset;
        _orbits = orbits;
        _camera = camera;
        _capture = capture;

    }

    public string Title => "Scenario";

    public bool Visible { get; set; } = true;

    public void Draw() {

        ImGui.SetNextWindowPos(new float2(730, 30), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Scenario"u8)) {

            if (ImGui.Button("Reset: low Earth orbit"u8)) {

                _reset(Scenario.LowOrbit);

            }

            ImGui.SameLine();

            if (ImGui.Button("Reset: lunar transfer"u8)) {

                _reset(Scenario.LunarTransfer);

            }

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

            if (ImGui.Button("Capture screenshot (F12)"u8)) {

                _capture();

            }

        }

        ImGui.End();

    }

}
