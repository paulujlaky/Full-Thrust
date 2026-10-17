using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Backends;

namespace FullThrust.Game.Rendering;

public sealed class ImGuiPass : IRenderPass {

    private readonly VulkanBackend _backend;

    public ImGuiPass(VulkanBackend backend) {

        _backend = backend;

    }

    public string Name => "ImGui";

    public RenderStage Stage => RenderStage.Overlay;

    public bool Enabled { get; set; } = true;

    public void Record(in FrameContext frame) {

        var drawData = ImGui.GetDrawData();

        if (drawData.IsNull()) {

            return;

        }

        _backend.RenderDrawData(drawData, frame.CommandBuffer);

    }

    // The backend is owned by the debug UI host, which outlives the renderer's passes.
    public void Dispose() {

    }

}
