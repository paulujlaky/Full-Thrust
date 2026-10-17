using Brutal.ImGuiApi;
using Brutal.Numerics;
using FullThrust.Core.Craft;
using FullThrust.Core.Diagnostics;
using FullThrust.Core.Ecs;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;
using FullThrust.Game.Rendering;
using FullThrust.Game.Scene;

namespace FullThrust.Game.Debug;

public sealed class SimulationPanel : IDebugPanel {

    private readonly World _world;
    private readonly SimulationClock _clock;

    private readonly FloatingOrigin _origin;
    private readonly FrameStats _stats;

    private readonly DemoScene _scene;
    private readonly DebugShapePass _shapes;

    public SimulationPanel(World world, SimulationClock clock, FloatingOrigin origin, FrameStats stats, DemoScene scene, DebugShapePass shapes) {

        _world = world;
        _clock = clock;
        _origin = origin;
        _stats = stats;
        _scene = scene;
        _shapes = shapes;

    }

    public string Title => "Simulation";

    public bool Visible { get; set; } = true;

    public void Draw() {

        ImGui.SetNextWindowPos(new float2(10, 330), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Simulation"u8)) {

            ImGui.SeparatorText("Clock"u8);
            ImGui.Text($"UT {_clock.UniversalTime:F2} s  tick {_clock.Tick}");
            ImGui.Text($"Fixed dt {_clock.FixedDeltaTime * 1000:F1} ms  steps/frame {_clock.StepsLastFrame}  dropped {_clock.DroppedSteps}");
            ImGui.Text($"Warp x{_clock.Warp.Rate:F0} ({_clock.Warp.Mode})  scale x{_clock.TimeScale:F2}{(_clock.Paused ? "  PAUSED" : "")}");

            ImGui.SeparatorText("World"u8);
            ImGui.Text($"Entities {_world.AliveCount} / capacity {_world.Capacity}");
            ImGui.Text($"Debug draws {_shapes.DrawCount}");

            ImGui.SeparatorText("Floating origin"u8);

            var o = _origin.Origin;

            ImGui.Text($"Origin ({o.X:F1}, {o.Y:F1}, {o.Z:F1})");
            ImGui.Text($"Shifts {_origin.ShiftCount}  threshold {_origin.RecenterDistance:F0} m  {(_origin.Enabled ? "enabled" : "DISABLED")}");

            DrawCraft();

            ImGui.SeparatorText("Managed allocations (main thread)"u8);
            ImGui.Text($"Last frame {_stats.AllocatedBytesLastFrame} B  frames with allocs {_stats.FramesWithAllocations}");
            ImGui.Text($"GC gen0 {GC.CollectionCount(0)}  gen1 {GC.CollectionCount(1)}  gen2 {GC.CollectionCount(2)}");

            if (ImGui.Button("Reset alloc counter"u8)) {

                _stats.ResetAllocationCounter();

            }

        }

        ImGui.End();

    }

    private void DrawCraft() {

        var craft = _scene.Craft;

        if (!_world.IsAlive(craft)) {

            return;

        }

        ref readonly var transform = ref _world.Get<Transform>(craft);
        ref readonly var velocity = ref _world.Get<Velocity>(craft);
        ref readonly var control = ref _world.Get<CraftControl>(craft);

        var p = transform.Position;
        var local = _origin.ToLocal(p);

        ImGui.SeparatorText("Craft"u8);
        ImGui.Text($"World ({p.X:F3}, {p.Y:F3}, {p.Z:F3})");
        ImGui.Text($"Local ({local.X:F3}, {local.Y:F3}, {local.Z:F3})");
        ImGui.Text($"Inertial speed {velocity.Linear.Length():F2} m/s");
        ImGui.Text($"Throttle {control.Throttle * 100:F0}%  P {control.Pitch:F0} Y {control.Yaw:F0} R {control.Roll:F0}");

    }

}
