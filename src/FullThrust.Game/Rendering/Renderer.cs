using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

public sealed class Renderer : IDisposable {

    public const int FramesInFlight = 2;

    private const int ShadowResolution = 2048;

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

    public Renderer(VulkanContext context, int2 framebufferSize, bool vsync, SceneLighting lighting) {

        _context = context;
        _vsync = vsync;

        Lighting = lighting;

        var device = context.Device;

        Swapchain = new Swapchain(context, framebufferSize, vsync);
        Hdr = new HdrTarget(device, Swapchain.Extent);
        Frames = new FrameResources(device);
        Shadows = new ShadowMaps(device, ShadowResolution);
        Prepass = new DepthPrepass();

        Frames.WriteImage(GlobalBindings.ShadowMap, Shadows.View, Shadows.Sampler);

        AddPass(Shadows);
        AddPass(Prepass);
        Capture = new FrameCapture(context);

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

    public HdrTarget Hdr { get; }

    public FrameResources Frames { get; }

    public ShadowMaps Shadows { get; }

    public DepthPrepass Prepass { get; }

    public SceneLighting Lighting { get; }

    public FrameCapture Capture { get; }

    // Opaque and Transparent passes are built against this; Post and Overlay passes against Swapchain.RenderPass.
    public VkRenderPass SceneRenderPass => Hdr.OpaquePass;

    // Depth-only pipelines built against this work for both the prepass and the shadow maps.
    public VkRenderPass DepthRenderPass => Hdr.DepthPass;

    // Linear radiance before exposure; space is close to black.
    public float4 ClearColor { get; set; } = new(0, 0, 0, 1);

    public IReadOnlyList<IRenderPass> Passes => _passes;

    public long FramesRendered { get; private set; }

    public long SwapchainRecreations { get; private set; }

    public double Seconds { get; set; }

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

    // Passes run in stage order, then in the order they were added.
    public void AddPass(IRenderPass pass) {

        var index = _passes.Count;

        while (index > 0 && _passes[index - 1].Stage > pass.Stage) {

            index--;

        }

        _passes.Insert(index, pass);

    }

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

        WriteFrameUniforms(view, projection);

        var cmd = _commandBuffers[_frame];

        cmd.Reset(VkCommandBufferResetFlags.None);
        cmd.Begin(new VkCommandBufferBeginInfo { Flags = VkCommandBufferUsageFlags.OneTimeSubmitBit });

        var extent = Swapchain.Extent;
        var frame = new FrameContext(cmd, _frame, extent, view, projection, Frames.Set(_frame));

        for (var i = 0; i < _passes.Count; i++) {

            if (_passes[i].Enabled && _passes[i] is IFrameUploader uploader) {

                uploader.Upload(frame);

            }

        }

        RecordOutside(frame, RenderStage.Prepare);

        // Reverse-Z: the far plane is depth 0.
        BeginRenderPass(cmd, Hdr.DepthPass, Hdr.DepthFramebuffer, extent, [new VkClearValue { DepthStencil = new VkClearDepthStencilValue { Depth = 0.0f } }]);
        RecordStages(frame, RenderStage.Depth, RenderStage.Depth);
        cmd.EndRenderPass();

        RecordOutside(frame, RenderStage.AfterDepth);

        BeginRenderPass(cmd, Hdr.OpaquePass, Hdr.Framebuffer, extent, [new VkClearValue { Color = new VkClearColorValue { Float32 = ClearColor } }]);
        RecordStages(frame, RenderStage.Opaque, RenderStage.Opaque);
        cmd.EndRenderPass();

        RecordOutside(frame, RenderStage.AfterOpaque);

        if (AnyEnabled(RenderStage.Transparent)) {

            BeginRenderPass(cmd, Hdr.TransparentPass, Hdr.Framebuffer, extent, []);
            RecordStages(frame, RenderStage.Transparent, RenderStage.Transparent);
            cmd.EndRenderPass();

        }

        RecordOutside(frame, RenderStage.PostProcess);

        BeginRenderPass(cmd, Swapchain.RenderPass, Swapchain.Framebuffer(imageIndex), extent, [new VkClearValue { Color = new VkClearColorValue { Float32 = new float4(0, 0, 0, 1) } }]);
        RecordStages(frame, RenderStage.Post, RenderStage.Overlay);
        cmd.EndRenderPass();

        if (Capture.IsPending && Swapchain.SupportsCapture) {

            Capture.Record(cmd, Swapchain.Image(imageIndex), Swapchain.Extent, Swapchain.SurfaceFormat.Format);

        }

        cmd.End();

        var renderFinished = _renderFinished[imageIndex];

        _context.GraphicsQueue.Submit([_imageAvailable[_frame]], [VkPipelineStageFlags.ColorAttachmentOutputBit], [cmd], [renderFinished], fence);

        Present(renderFinished, imageIndex);
        Capture.CompleteAfterSubmit();

        _frame = (_frame + 1) % FramesInFlight;
        FramesRendered++;

        return true;

    }

    private void RecordStages(in FrameContext frame, RenderStage first, RenderStage last) {

        var cmd = frame.CommandBuffer;
        var extent = frame.Extent;

        cmd.SetViewport(0, [new VkViewport { X = 0, Y = 0, Width = extent.Width, Height = extent.Height, MinDepth = 0, MaxDepth = 1 }]);
        cmd.SetScissor(0, [new VkRect2D(extent)]);

        for (var i = 0; i < _passes.Count; i++) {

            var pass = _passes[i];

            if (pass.Enabled && pass.Stage >= first && pass.Stage <= last) {

                pass.Record(frame);

            }

        }

    }

    private void RecordOutside(in FrameContext frame, RenderStage stage) {

        for (var i = 0; i < _passes.Count; i++) {

            if (_passes[i].Enabled && _passes[i].Stage == stage) {

                _passes[i].Record(frame);

            }

        }

    }

    private bool AnyEnabled(RenderStage stage) {

        for (var i = 0; i < _passes.Count; i++) {

            if (_passes[i].Enabled && _passes[i].Stage == stage) {

                return true;

            }

        }

        return false;

    }

    private void WriteFrameUniforms(float4x4 view, float4x4 projection) {

        var lighting = Lighting;
        var viewProjection = view * projection;

        float4x4.Invert(view, out var inverseView);
        float4x4.Invert(viewProjection, out var inverseViewProjection);

        var uniforms = new FrameUniforms {

            View = view,
            Projection = projection,
            ViewProjection = viewProjection,
            InverseViewProjection = inverseViewProjection,

            CameraPosition = new float4(inverseView.W.X, inverseView.W.Y, inverseView.W.Z, 1),

            SunDirection = new float4(lighting.SunDirection, lighting.SunCosAngularRadius),
            SunIlluminance = new float4(lighting.SunIlluminance, 0),

            AmbientUp = new float4(lighting.AmbientUp, lighting.MinimumAmbientLux * lighting.AmbientScale),
            SkyIrradiance = new float4(lighting.SkyIrradiance, 0),
            GroundIrradiance = new float4(lighting.GroundIrradiance, lighting.AmbientScale),

            Exposure = new float4(lighting.Exposure, lighting.Ev100, (float)lighting.Tonemapper, (float)(Seconds % 3600.0)),

            Viewport = new float4(Swapchain.Extent.Width, Swapchain.Extent.Height, 1.0f / Swapchain.Extent.Width, 1.0f / Swapchain.Extent.Height),

        };

        Shadows.Update(view, projection, lighting.SunDirection);

        for (var i = 0; i < _passes.Count; i++) {

            if (_passes[i] is IFrameUniformsWriter writer) {

                writer.Fill(ref uniforms, _passes[i].Enabled);

            }

        }

        Frames.Write(_frame, uniforms);

    }

    private static unsafe void BeginRenderPass(CommandBuffer cmd, VkRenderPass renderPass, VkFramebuffer framebuffer, VkExtent2D extent, ReadOnlySpan<VkClearValue> clears) {

        fixed (VkClearValue* pClears = clears) {

            cmd.BeginRenderPass(new VkRenderPassBeginInfo {

                RenderPass = renderPass,
                Framebuffer = framebuffer,
                RenderArea = new VkRect2D(extent),
                ClearValueCount = clears.Length,
                ClearValues = pClears,

            }, VkSubpassContents.Inline);

        }

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

        var extent = Swapchain.Extent;

        if (extent.Width != Hdr.Extent.Width || extent.Height != Hdr.Extent.Height) {

            Hdr.Resize(extent);

            for (var i = 0; i < _passes.Count; i++) {

                if (_passes[i] is IResizeListener listener) {

                    listener.OnResize(extent);

                }

            }

        }

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

        Capture.Dispose();
        Frames.Dispose();
        Hdr.Dispose();
        Swapchain.Dispose();

    }

}
