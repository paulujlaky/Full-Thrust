using Brutal.ImGuiApi;
using Brutal.Numerics;
using FullThrust.Core.Diagnostics;
using FullThrust.Game.Rendering;

namespace FullThrust.Game.Debug;

public sealed class FrameTimingPanel : IDebugPanel {

    private readonly FrameStats _stats;
    private readonly FrameProfile _profile;

    private readonly Renderer _renderer;
    private readonly VulkanContext _context;

    public FrameTimingPanel(FrameStats stats, FrameProfile profile, Renderer renderer, VulkanContext context) {

        _stats = stats;
        _profile = profile;
        _renderer = renderer;
        _context = context;

    }

    public string Title => "Frame Timing";

    public bool Visible { get; set; } = true;

    public void Draw() {

        ImGui.SetNextWindowPos(new float2(10, 30), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Frame Timing"u8)) {

            ImGui.Text($"{_stats.Fps:F0} FPS  |  {_stats.AverageMs:F2} ms avg");
            ImGui.Text($"min {_stats.MinMs:F2} ms  max {_stats.MaxMs:F2} ms");

            ImGui.PlotLines("##frametimes"u8, _stats.History, _stats.HistoryOffset, "frame ms"u8, 0, 33.3f, new float2(0, 60));

            ImGui.SeparatorText("CPU breakdown"u8);
            ImGui.Text($"Simulation {_profile.SimulationMs:F3} ms");
            ImGui.Text($"UI build   {_profile.UiMs:F3} ms");
            ImGui.Text($"Render     {_profile.RenderMs:F3} ms");
            ImGui.Text($"Allocated sim {_profile.SimulationBytes} B  ui {_profile.UiBytes} B  render {_profile.RenderBytes} B");

            ImGui.SeparatorText("GPU"u8);
            ImGui.TextWrapped(_context.DeviceName);

            var extent = _renderer.Swapchain.Extent;

            ImGui.Text($"{extent.Width}x{extent.Height}  {_renderer.Swapchain.PresentMode}");
            ImGui.Text($"Validation: {(_context.ValidationEnabled ? "on" : "off")}  Recreations: {_renderer.SwapchainRecreations}");

            var profiler = _renderer.Profiler;

            ImGui.SeparatorText("GPU passes"u8);
            ImGui.Text($"Frame {profiler.FrameMs:F2} ms");

            for (var i = 0; i < _renderer.Passes.Count && i < GpuProfiler.MaxScopes; i++) {

                var pass = _renderer.Passes[i];

                if (pass.Enabled) {

                    ImGui.Text($"{pass.Name,-22} {profiler.ScopeMs(i):F3} ms");

                }

            }

        }

        ImGui.End();

    }

}

public sealed class FrameProfile {

    public float SimulationMs;
    public float UiMs;
    public float RenderMs;

    public long SimulationBytes;
    public long UiBytes;
    public long RenderBytes;

    public void Accumulate(long simulationBytes, long uiBytes, long renderBytes) {

        SimulationBytes += simulationBytes;
        UiBytes += uiBytes;
        RenderBytes += renderBytes;

    }

    public void ResetAllocations() {

        SimulationBytes = 0;
        UiBytes = 0;
        RenderBytes = 0;

    }

}
