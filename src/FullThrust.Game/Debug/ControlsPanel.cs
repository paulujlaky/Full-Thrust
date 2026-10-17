using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace FullThrust.Game.Debug;

public sealed class ControlsPanel : IDebugPanel {

    public string Title => "Controls";

    public bool Visible { get; set; } = true;

    public void Draw() {

        ImGui.SetNextWindowPos(new float2(1180, 660), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Controls"u8)) {

            ImGui.BulletText("W/S pitch, A/D yaw, Q/E roll"u8);
            ImGui.BulletText("Shift/Ctrl throttle up/down, Z full, X cut"u8);
            ImGui.BulletText("I/K J/L H/N translate"u8);
            ImGui.BulletText("Right mouse drag: orbit camera, wheel: zoom"u8);
            ImGui.BulletText("Tab: cycle camera focus (craft, Earth, Moon)"u8);
            ImGui.BulletText(". / , : time warp up/down, / : stop warp"u8);
            ImGui.BulletText("Space: pause, F3: overlay, F12: screenshot, Esc: quit"u8);

        }

        ImGui.End();

    }

}
