using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Backends;
using Brutal.VulkanApi;
using FullThrust.Game.Rendering;

namespace FullThrust.Game.Debug;

public interface IDebugPanel {

    string Title { get; }

    bool Visible { get; set; }

    void Draw();

}

public sealed class DebugUi : IDisposable {

    private readonly List<IDebugPanel> _panels = [];

    private readonly GlfwBackend _platform;

    public DebugUi(GlfwWindow window, VulkanContext context, Swapchain swapchain) {

        ImGui.CreateContext();

        var io = ImGui.GetIO();

        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable | ImGuiConfigFlags.NavEnableKeyboard;

        ImGui.StyleColorsDark();

        // Platform backend must exist first: it installs the surface hook the Vulkan backend asserts on.
        _platform = new GlfwBackend(window, GlfwBackend.ClientApi.Vulkan);

        Renderer = new VulkanBackend(new VulkanBackend.CreateInfo {

            Device = context.Device,
            GraphicsQueue = context.GraphicsQueue,
            RenderPass = swapchain.RenderPass,
            SubPass = 0,
            MinImageCount = swapchain.MinImageCount,
            ImageCount = Math.Max(swapchain.ImageCount, swapchain.MinImageCount),
            SampleCount = VkSampleCountFlags._1Bit,
            DescriptorPoolSize = 64,

        });

    }

    public VulkanBackend Renderer { get; }

    public bool Visible { get; set; } = true;

    public bool WantsMouse => ImGui.GetIO().WantCaptureMouse;

    public bool WantsKeyboard => ImGui.GetIO().WantCaptureKeyboard;

    public IReadOnlyList<IDebugPanel> Panels => _panels;

    public void AddPanel(IDebugPanel panel) => _panels.Add(panel);

    public void BuildFrame() {

        Renderer.NewFrame();
        _platform.NewFrame();
        ImGui.NewFrame();

        if (Visible) {

            DrawMenu();

            for (var i = 0; i < _panels.Count; i++) {

                var panel = _panels[i];

                if (panel.Visible) {

                    panel.Draw();

                }

            }

        }

        ImGui.Render();

    }

    // Interpolated ImGui strings live in a shared arena that is never reset automatically.
    public void EndFrame() => ImString.SharedStorage.Clear();

    private void DrawMenu() {

        if (!ImGui.BeginMainMenuBar()) {

            return;

        }

        if (ImGui.BeginMenu("Panels"u8)) {

            for (var i = 0; i < _panels.Count; i++) {

                var panel = _panels[i];
                var visible = panel.Visible;

                if (ImGui.Checkbox(panel.Title, ref visible)) {

                    panel.Visible = visible;

                }

            }

            ImGui.EndMenu();

        }

        ImGui.TextDisabled("F3 toggles overlay"u8);

        ImGui.EndMainMenuBar();

    }

    public void Dispose() {

        Renderer.Dispose();
        _platform.Dispose();

        ImGui.DestroyContext();

    }

}
