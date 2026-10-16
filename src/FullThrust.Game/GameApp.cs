using System.Diagnostics;
using Brutal.GlfwApi;
using Brutal.Logging;
using Brutal.Numerics;
using FullThrust.Core.Craft;
using FullThrust.Core.Diagnostics;
using FullThrust.Core.Ecs;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;
using FullThrust.Game.Controls;
using FullThrust.Game.Debug;
using FullThrust.Game.Platform;
using FullThrust.Game.Rendering;
using FullThrust.Game.Scene;

namespace FullThrust.Game;

public sealed class GameOptions {

    public int2 WindowSize = new(1600, 900);

    public bool Validation = true;

    public bool VSync = true;

    public bool Overlay = true;

    // Non-zero runs a fixed number of frames at full throttle, logs a summary and exits.
    public int SmokeFrames;

}

public sealed class GameApp : IDisposable {

    private static readonly LogCategory Log = LogCategory.Make("Game");

    private const double SmokeWarmupSeconds = 3.0;

    private readonly GameOptions _options;

    private readonly GlfwWindow _window;
    private readonly InputState _input;

    private readonly VulkanContext _vulkan;
    private readonly Renderer _renderer;
    private readonly DebugUi _debugUi;

    private readonly World _world = new();
    private readonly SystemScheduler _systems = new();
    private readonly SimulationClock _clock = new(new RealTimeWarp());
    private readonly FloatingOrigin _origin = new();

    private readonly DemoScene _scene = new();
    private readonly CameraController _camera;
    private readonly FlightInputSystem _flightInput;

    private readonly FrameStats _stats = new();
    private readonly FrameProfile _profile = new();

    public GameApp(GameOptions options) {

        _options = options;

        if (!Glfw.Init()) {

            throw new InvalidOperationException("Failed to initialise GLFW.");

        }

        Glfw.WindowHint(GlfwWindowHint.ClientApi, Glfw.Constants.GLFW_NO_API);
        Glfw.WindowHint(GlfwWindowHint.Resizable, Glfw.Constants.GLFW_TRUE);

        _window = Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = "Full Thrust", Size = options.WindowSize });
        _input = new InputState(_window);

        _vulkan = new VulkanContext(_window, options.Validation);
        _renderer = new Renderer(_vulkan, _window.FramebufferSize, options.VSync);
        _debugUi = new DebugUi(_window, _vulkan, _renderer.Swapchain) { Visible = options.Overlay };

        _scene.Populate(_world);

        _camera = new CameraController(_input, new Camera()) { Target = _scene.Craft, UpReference = _scene.Planet };
        _flightInput = new FlightInputSystem(_input) { ControlledCraft = _scene.Craft };

        _systems
            .Add(SystemPhase.Update, _flightInput)
            .Add(SystemPhase.FixedUpdate, new CraftControlSystem())
            .Add(SystemPhase.FixedUpdate, new KinematicsSystem());

        var shapes = CreatePasses();

        _debugUi.AddPanel(new FrameTimingPanel(_stats, _profile, _renderer, _vulkan));
        _debugUi.AddPanel(new SimulationPanel(_world, _clock, _origin, _stats, _scene, shapes));
        _debugUi.AddPanel(new TweaksPanel(_clock, _origin, _renderer, shapes, _camera, _flightInput));
        _debugUi.AddPanel(new ControlsPanel());

        _origin.ShiftTo(_world.Get<Transform>(_scene.Craft).Position);

    }

    private DebugShapePass CreatePasses() {

        var shaderRoot = Path.Combine(AppContext.BaseDirectory, "Shaders");

        using var shaders = new ShaderLibrary(_vulkan.Device, shaderRoot);

        var shapes = new DebugShapePass(_vulkan.Device, _renderer.Swapchain.RenderPass, shaders, _world, _origin);

        _renderer.AddPass(shapes);
        _renderer.AddPass(new ImGuiPass(_debugUi.Renderer));

        return shapes;

    }

    public void Run() {

        var smoke = _options.SmokeFrames > 0;

        if (smoke) {

            _world.Get<CraftControl>(_scene.Craft).Throttle = 1;

        }

        var start = Stopwatch.GetTimestamp();
        var last = start;

        var warmupFrames = -1L;
        var allocatedAfterWarmup = 0L;

        while (!_window.ShouldClose) {

            Glfw.PollEvents();

            var now = Stopwatch.GetTimestamp();
            var frameSeconds = Math.Min(Stopwatch.GetElapsedTime(last, now).TotalSeconds, 0.25);

            last = now;

            _stats.BeginFrame();

            Frame(frameSeconds);

            _stats.EndFrame(frameSeconds);

            if (!smoke) {

                continue;

            }

            // Tier-0 JIT code boxes enum HasFlag calls, so only measure once tiering has settled.
            if (warmupFrames < 0) {

                if (Stopwatch.GetElapsedTime(start).TotalSeconds >= SmokeWarmupSeconds) {

                    warmupFrames = _stats.FrameCount;
                    _stats.ResetAllocationCounter();
                    _profile.ResetAllocations();

                }

                continue;

            }

            allocatedAfterWarmup += _stats.AllocatedBytesLastFrame;

            if (_stats.FrameCount - warmupFrames >= _options.SmokeFrames) {

                LogSmokeSummary(warmupFrames, allocatedAfterWarmup);
                _window.ShouldClose = true;

            }

        }

    }

    private void LogSmokeSummary(long warmupFrames, long allocatedAfterWarmup) {

        var craft = _world.Get<Transform>(_scene.Craft).Position;

        Log.Info($"Smoke: {_stats.FrameCount} frames, avg {_stats.AverageMs:F2} ms ({_stats.Fps:F0} FPS), max {_stats.MaxMs:F2} ms");
        Log.Info($"Smoke: after {warmupFrames} warmup frames ({SmokeWarmupSeconds} s), {_stats.FramesWithAllocations} of {_options.SmokeFrames} measured frames allocated {allocatedAfterWarmup} B on the main thread");
        Log.Info($"Smoke: allocations by section: sim {_profile.SimulationBytes} B, ui {_profile.UiBytes} B, render {_profile.RenderBytes} B");
        Log.Info($"Smoke: UT {_clock.UniversalTime:F2} s, origin shifts {_origin.ShiftCount}, altitude {(craft.Length() - DemoScene.PlanetRadius) / 1000:F2} km");
        Log.Info($"Smoke: {_renderer.FramesRendered} frames presented, {_renderer.SwapchainRecreations} swapchain recreations, present mode {_renderer.Swapchain.PresentMode}");

    }

    private void Frame(double realDeltaTime) {

        _input.UiWantsMouse = _debugUi.Visible && _debugUi.WantsMouse;
        _input.UiWantsKeyboard = _debugUi.Visible && _debugUi.WantsKeyboard;
        _input.BeginFrame();

        HandleShortcuts();

        var t0 = Stopwatch.GetTimestamp();
        var a0 = GC.GetAllocatedBytesForCurrentThread();

        var steps = _clock.Advance(realDeltaTime);

        _systems.Run(SystemPhase.Update, _world, _clock.FrameTime);

        for (var i = 0; i < steps; i++) {

            _systems.Run(SystemPhase.FixedUpdate, _world, _clock.NextFixedStep());

        }

        _systems.Run(SystemPhase.LateUpdate, _world, _clock.FrameTime);

        _origin.Update(_world.Get<Transform>(_scene.Craft).Position);
        _camera.Update(_world);

        var t1 = Stopwatch.GetTimestamp();
        var a1 = GC.GetAllocatedBytesForCurrentThread();

        _debugUi.BuildFrame();

        var t2 = Stopwatch.GetTimestamp();
        var a2 = GC.GetAllocatedBytesForCurrentThread();

        var size = _window.FramebufferSize;
        var aspect = size.Y > 0 ? (float)size.X / size.Y : 1.0f;

        _renderer.Render(size, _camera.Camera.ViewMatrix(_origin), _camera.Camera.ProjectionMatrix(aspect));
        _debugUi.EndFrame();

        var t3 = Stopwatch.GetTimestamp();
        var a3 = GC.GetAllocatedBytesForCurrentThread();

        _profile.SimulationMs = (float)Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds;
        _profile.UiMs = (float)Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds;
        _profile.RenderMs = (float)Stopwatch.GetElapsedTime(t2, t3).TotalMilliseconds;

        _profile.Accumulate(a1 - a0, a2 - a1, a3 - a2);

        _input.EndFrame();

    }

    private void HandleShortcuts() {

        if (_input.WasPressedRaw(GlfwKey.Escape)) {

            _window.ShouldClose = true;

        }

        if (_input.WasPressedRaw(GlfwKey.F3)) {

            _debugUi.Visible = !_debugUi.Visible;

        }

        if (_input.WasPressed(GlfwKey.Space)) {

            _clock.Paused = !_clock.Paused;

        }

    }

    public void Dispose() {

        _renderer.Dispose();
        _debugUi.Dispose();
        _vulkan.Dispose();

        _input.Dispose();
        _window.Dispose();

        Glfw.Terminate();

    }

}
