using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Extensions;
using Brutal.Numerics;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;
using FullThrust.Game.Controls;
using FullThrust.Game.Rendering;

namespace FullThrust.Game.Debug;

public sealed class TweaksPanel : IDebugPanel {

    private readonly SimulationClock _clock;
    private readonly FloatingOrigin _origin;

    private readonly Renderer _renderer;
    private readonly DebugShapePass _shapes;

    private readonly CameraController _camera;
    private readonly FlightInputSystem _flight;

    public TweaksPanel(SimulationClock clock, FloatingOrigin origin, Renderer renderer, DebugShapePass shapes, CameraController camera, FlightInputSystem flight) {

        _clock = clock;
        _origin = origin;
        _renderer = renderer;
        _shapes = shapes;
        _camera = camera;
        _flight = flight;

    }

    public string Title => "Tweakables";

    public bool Visible { get; set; } = true;

    public void Draw() {

        ImGui.SetNextWindowPos(new float2(420, 30), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Tweakables"u8)) {

            DrawSimulation();
            DrawOrigin();
            DrawCamera();
            DrawRendering();

        }

        ImGui.End();

    }

    private void DrawSimulation() {

        if (!ImGui.CollapsingHeader("Simulation"u8, ImGuiTreeNodeFlags.DefaultOpen)) {

            return;

        }

        var paused = _clock.Paused;

        if (ImGui.Checkbox("Paused"u8, ref paused)) {

            _clock.Paused = paused;

        }

        var scale = _clock.TimeScale;

        if (ImGuiEx.SliderScalar("Time scale"u8, ImGuiDataType.Double, ref scale, 0.0, 4.0, "%.2f"u8)) {

            _clock.TimeScale = scale;

        }

        var steps = _clock.MaxStepsPerFrame;

        if (ImGui.SliderInt("Max steps/frame"u8, ref steps, 1, 32)) {

            _clock.MaxStepsPerFrame = steps;

        }

        var throttleRate = _flight.ThrottleRate;

        if (ImGui.SliderFloat("Throttle rate"u8, ref throttleRate, 0.1f, 3.0f, "%.2f /s"u8)) {

            _flight.ThrottleRate = throttleRate;

        }

    }

    private void DrawOrigin() {

        if (!ImGui.CollapsingHeader("Floating origin"u8, ImGuiTreeNodeFlags.DefaultOpen)) {

            return;

        }

        var enabled = _origin.Enabled;

        if (ImGui.Checkbox("Enabled (disable to see float jitter)"u8, ref enabled)) {

            _origin.Enabled = enabled;

        }

        var distance = _origin.RecenterDistance;

        if (ImGuiEx.SliderScalar("Recenter distance"u8, ImGuiDataType.Double, ref distance, 10.0, 100_000.0, "%.0f m"u8, ImGuiSliderFlags.Logarithmic)) {

            _origin.RecenterDistance = distance;

        }

    }

    private void DrawCamera() {

        if (!ImGui.CollapsingHeader("Camera"u8, ImGuiTreeNodeFlags.DefaultOpen)) {

            return;

        }

        var camera = _camera.Camera;

        ImGui.SliderAngle("Vertical FOV"u8, ref camera.VerticalFov, 20, 110);
        ImGui.SliderFloat("Near plane"u8, ref camera.NearPlane, 0.01f, 10f, "%.2f m"u8, ImGuiSliderFlags.Logarithmic);

        var sensitivity = _camera.MouseSensitivity;

        if (ImGui.SliderFloat("Mouse sensitivity"u8, ref sensitivity, 0.0005f, 0.02f, "%.4f"u8)) {

            _camera.MouseSensitivity = sensitivity;

        }

        var rig = _camera.Rig;

        ImGuiEx.SliderScalar("Distance"u8, ImGuiDataType.Double, ref rig.Distance, rig.MinDistance, rig.MaxDistance, "%.1f m"u8, ImGuiSliderFlags.Logarithmic);

    }

    private void DrawRendering() {

        if (!ImGui.CollapsingHeader("Rendering"u8, ImGuiTreeNodeFlags.DefaultOpen)) {

            return;

        }

        var vsync = _renderer.VSync;

        if (ImGui.Checkbox("VSync"u8, ref vsync)) {

            _renderer.VSync = vsync;

        }

        var passes = _renderer.Passes;

        for (var i = 0; i < passes.Count; i++) {

            var pass = passes[i];

            if (pass is ImGuiPass) {

                continue;

            }

            var passEnabled = pass.Enabled;

            if (ImGui.Checkbox(pass.Name, ref passEnabled)) {

                pass.Enabled = passEnabled;

            }

        }

        var sun = _shapes.SunDirection;

        if (ImGui.SliderFloat3("Sun direction"u8, ref sun, -1, 1)) {

            _shapes.SunDirection = float3.NormalizeOrZero(sun) == float3.Zero ? _shapes.SunDirection : float3.Normalize(sun);

        }

        var clear = _renderer.ClearColor.XYZ;

        if (ImGui.ColorEdit3("Clear color"u8, ref clear)) {

            _renderer.ClearColor = new float4(clear, 1);

        }

    }

}
