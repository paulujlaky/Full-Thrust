using Brutal.ImGuiApi;
using Brutal.Numerics;
using FullThrust.Core.Simulation;

namespace FullThrust.Game.Debug;

public sealed class TimeWarpPanel : IDebugPanel {

    private readonly TimeWarpController _warp;
    private readonly SimulationClock _clock;

    public TimeWarpPanel(TimeWarpController warp, SimulationClock clock) {

        _warp = warp;
        _clock = clock;

    }

    public string Title => "Time Warp";

    public bool Visible { get; set; } = true;

    public void Draw() {

        ImGui.SetNextWindowPos(new float2(1180, 520), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Time Warp"u8)) {

            var (d, h, m, s) = TimeFormat.Split(_clock.UniversalTime);

            ImGui.Text($"UT {d}d {h:00}:{m:00}:{s:00}");
            ImGui.Text($"Warp x{_warp.Rate:F0}  mode {_warp.Mode}{(_clock.Paused ? "  PAUSED" : "")}");

            var rates = _warp.RailsRates;

            for (var i = 0; i < rates.Length; i++) {

                if (i > 0) {

                    ImGui.SameLine();

                }

                var selected = _warp.Level == i;

                if (ImGui.RadioButton($"{rates[i]:0}##warp{i}", selected)) {

                    _warp.SetLevel(i);

                }

            }

            if (_warp.BlockedReason is { Length: > 0 } reason) {

                ImGui.TextColored(new float4(1, 0.7f, 0.3f, 1), reason);

            }

            ImGui.TextDisabled(", / . change warp, / stops"u8);

        }

        ImGui.End();

    }

}
