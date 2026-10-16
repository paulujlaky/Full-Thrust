using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

public sealed class Renderer : IDisposable {

    public const int FramesInFlight = 2;

    private readonly VulkanContext _context;

    private readonly List<IRenderPass> _passes = [];

    private readonly VkCommandPool _commandPool;
    private readonly CommandBuffer[] _commandBuffers = new CommandBuffer[FramesInFlight];

    private readonly VkFence[] _inFlight = new VkFence[FramesInFlight];
    private readonly VkSemaphore[] _imageAvailable = new VkSemaphore[FramesInFlight];

    // One per swapchain image: presentation may still hold the semaphore of an earlier frame.
    private VkSemaphore[] _renderFinished = [];

    private int _frame;

    private bool _recreateRequested;
    private bool _vsync;

    public Renderer(VulkanContext context, int2 framebufferSize, bool vsync) {

        _context = context;
        _vsync = vsync;

        var device = context.Device;

        Swapchain = new Swapchain(context, framebufferSize, vsync);

        _commandPool = device.CreateCommandPool(new VkCommandPoolCreateInfo {

            QueueFamilyIndex = context.GraphicsFamily,
            Flags = VkCommandPoolCreateFlags.ResetCommandBufferBit,

        }, null);

        device.AllocateCommandBuffers(new VkCommandBufferAllocateInfo {

            CommandPool = _commandPool,
            Level = VkCommandBufferLevel.Primary,

        }, _commandBuffers);

        for (var i = 0; i < FramesInFlight; i++) {

            _inFlight[i] = device.CreateFence(new VkFenceCreateInfo { Flags = VkFenceCreateFlags.SignaledBit }, null);
            _imageAvailable[i] = device.CreateSemaphore(new VkSemaphoreCreateInfo(), null);

        }

        CreateRenderFinishedSemaphores();

    }

    public Swapchain Swapchain { get; }

    public float4 ClearColor { get; set; } = new(0.004f, 0.005f, 0.012f, 1);

    public IReadOnlyList<IRenderPass> Passes => _passes;

    public long FramesRendered { get; private set; }

    public long SwapchainRecreations { get; private set; }

    public bool VSync {

        get => _vsync;

        set {

            if (_vsync == value) {

                return;

            }

            _vsync = value;
            _recreateRequested = true;

        }

    }

    public void AddPass(IRenderPass pass) => _passes.Add(pass);

    public void RequestRecreate() => _recreateRequested = true;

    public bool Render(int2 framebufferSize, float4x4 view, float4x4 projection) {

        if (framebufferSize.X <= 0 || framebufferSize.Y <= 0) {

            return false;

        }

        var device = _context.Device;

        if (_recreateRequested) {

            Recreate(framebufferSize);

        }

        var fence = _inFlight[_frame];

        device.WaitForFences([fence], true, -1);

        int imageIndex;

        try {

            var acquire = device.AcquireNextImageKHR(Swapchain.Handle, -1, _imageAvailable[_frame], default, out imageIndex);

            if (acquire == VkResult.SuboptimalKHR) {

                _recreateRequested = true;

            }

        } catch (VkResultError e) when (e.Result == VkResult.ErrorOutOfDateKHR) {

            Recreate(framebufferSize);

            return false;

        }

        device.ResetFences([fence]);

        var cmd = _commandBuffers[_frame];

        cmd.Reset(VkCommandBufferResetFlags.None);
        cmd.Begin(new VkCommandBufferBeginInfo { Flags = VkCommandBufferUsageFlags.OneTimeSubmitBit });

        BeginRenderPass(cmd, imageIndex);

        var extent = Swapchain.Extent;

        cmd.SetViewport(0, [new VkViewport { X = 0, Y = 0, Width = extent.Width, Height = extent.Height, MinDepth = 0, MaxDepth = 1 }]);
        cmd.SetScissor(0, [new VkRect2D(extent)]);

        var frame = new FrameContext(cmd, _frame, extent, view, projection);

        for (var i = 0; i < _passes.Count; i++) {

            if (_passes[i].Enabled) {

                _passes[i].Record(frame);

            }

        }

        cmd.EndRenderPass();
        cmd.End();

        var renderFinished = _renderFinished[imageIndex];

        _context.GraphicsQueue.Submit([_imageAvailable[_frame]], [VkPipelineStageFlags.ColorAttachmentOutputBit], [cmd], [renderFinished], fence);

        Present(renderFinished, imageIndex);

        _frame = (_frame + 1) % FramesInFlight;
        FramesRendered++;

        return true;

    }

    private unsafe void BeginRenderPass(CommandBuffer cmd, int imageIndex) {

        var clears = stackalloc VkClearValue[2];

        clears[0] = new VkClearColorValue { Float32 = ClearColor };

        // Reverse-Z: the far plane is depth 0.
        clears[1] = new VkClearDepthStencilValue { Depth = 0.0f, Stencil = 0 };

        cmd.BeginRenderPass(new VkRenderPassBeginInfo {

            RenderPass = Swapchain.RenderPass,
            Framebuffer = Swapchain.Framebuffer(imageIndex),
            RenderArea = new VkRect2D(Swapchain.Extent),
            ClearValueCount = 2,
            ClearValues = clears,

        }, VkSubpassContents.Inline);

    }

    private unsafe void Present(VkSemaphore renderFinished, int imageIndex) {

        var swapchain = Swapchain.Handle;

        try {

            var result = _context.GraphicsQueue.PresentKHR(new VkPresentInfoKHR {

                WaitSemaphoreCount = 1,
                WaitSemaphores = &renderFinished,
                SwapchainCount = 1,
                Swapchains = &swapchain,
                ImageIndices = &imageIndex,

            });

            if (result == VkResult.SuboptimalKHR) {

                _recreateRequested = true;

            }

        } catch (VkResultError e) when (e.Result == VkResult.ErrorOutOfDateKHR) {

            _recreateRequested = true;

        }

    }

    private void Recreate(int2 framebufferSize) {

        Swapchain.Recreate(framebufferSize, _vsync);

        DestroyRenderFinishedSemaphores();
        CreateRenderFinishedSemaphores();

        _recreateRequested = false;
        SwapchainRecreations++;

    }

    private void CreateRenderFinishedSemaphores() {

        _renderFinished = new VkSemaphore[Swapchain.ImageCount];

        for (var i = 0; i < _renderFinished.Length; i++) {

            _renderFinished[i] = _context.Device.CreateSemaphore(new VkSemaphoreCreateInfo(), null);

        }

    }

    private void DestroyRenderFinishedSemaphores() {

        foreach (var semaphore in _renderFinished) {

            _context.Device.DestroySemaphore(semaphore, null);

        }

    }

    public void Dispose() {

        var device = _context.Device;

        device.WaitIdle();

        for (var i = _passes.Count - 1; i >= 0; i--) {

            _passes[i].Dispose();

        }

        DestroyRenderFinishedSemaphores();

        for (var i = 0; i < FramesInFlight; i++) {

            device.DestroyFence(_inFlight[i], null);
            device.DestroySemaphore(_imageAvailable[i], null);

        }

        device.DestroyCommandPool(_commandPool, null);

        Swapchain.Dispose();

    }

}
