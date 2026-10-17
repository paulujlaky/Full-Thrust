using System.Diagnostics;
using Brutal.GlfwApi;
using Brutal.Logging;
using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Craft;
using FullThrust.Core.Diagnostics;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
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

    public string SystemFile = Path.Combine("data", "celestial", "earth-moon.json");

    public Scenario Scenario = Scenario.LowOrbit;

    public int Focus;

    public double CameraDistance;

    public string CaptureDirectory = "captures";

    // Non-zero runs a scripted smoke test: warm up, measure this many frames, log a summary and exit.
    public int SmokeFrames;

    public double SmokeWarpRate = 1;

    public double SmokeThrottle;

    public int CaptureEvery;

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
    private readonly TimeWarpController _warp = new();
    private readonly SimulationClock _clock;
    private readonly FloatingOrigin _origin = new();

    private readonly CelestialSystem _bodies;
    private readonly OrbitEvents _orbitEvents = new();
    private readonly TrajectoryPredictor _predictor;

    private readonly DemoScene _scene;
    private readonly CameraController _camera;
    private readonly FlightInputSystem _flightInput;

    private readonly DebugLines _lines;
    private readonly OrbitDebugDraw _orbitDraw;

    private readonly FrameStats _stats = new();
    private readonly FrameProfile _profile = new();

    public GameApp(GameOptions options) {

        _options = options;
        _clock = new SimulationClock(_warp);

        _bodies = CelestialSystemLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, options.SystemFile), new KeplerPropagator());
        _predictor = new TrajectoryPredictor(_bodies, new PatchedConicSolver(_bodies));

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

        _scene = new DemoScene(_bodies);
        _scene.Populate(_world, options.Scenario, _clock.UniversalTime);

        _camera = new CameraController(_input, new Camera(), _scene);
        _flightInput = new FlightInputSystem(_input) { ControlledCraft = _scene.Craft };

        AddCameraTargets();

        _warp.AddConstraint(new ThrustWarpConstraint(_world));

        _systems
            .Add(SystemPhase.Update, _flightInput)
            .Add(SystemPhase.FixedUpdate, new CraftControlSystem())
            .Add(SystemPhase.FixedUpdate, new OrbitalThrustSystem(_bodies))
            .Add(SystemPhase.FixedUpdate, new KinematicsSystem())
            .Add(SystemPhase.LateUpdate, new CelestialBodySystem(_bodies))
            .Add(SystemPhase.LateUpdate, new OrbitPropagationSystem(_bodies, new PatchedConicSolver(_bodies), _orbitEvents));

        _lines = new DebugLines(_origin);
        _orbitDraw = new OrbitDebugDraw(_bodies, _predictor, _lines);

        var shapes = CreatePasses();

        _debugUi.AddPanel(new FrameTimingPanel(_stats, _profile, _renderer, _vulkan));
        _debugUi.AddPanel(new SimulationPanel(_world, _clock, _origin, _stats, _scene, shapes));
        _debugUi.AddPanel(new TweaksPanel(_clock, _origin, _renderer, shapes, _camera, _flightInput));
        _debugUi.AddPanel(new OrbitPanel(_world, _bodies, _clock, _scene, _predictor, _orbitEvents));
        _debugUi.AddPanel(new TimeWarpPanel(_warp, _clock));
        _debugUi.AddPanel(new ScenarioPanel(ResetScenario, _orbitDraw, _camera, CaptureNow));
        _debugUi.AddPanel(new ControlsPanel());

        _origin.ShiftTo(_world.Get<Transform>(_scene.Craft).Position);

    }

    private void AddCameraTargets() {

        _camera.AddTarget(new CameraTarget { Label = "Craft", Entity = _scene.Craft, DefaultDistance = 25, MinDistance = 2 });

        foreach (var body in _bodies.Bodies) {

            _camera.AddTarget(new CameraTarget {

                Label = body.Name,
                Entity = _scene.BodyEntity(body.Index),
                DefaultDistance = body.Radius * 4,
                MinDistance = body.Radius * 1.05,

            });

        }

        _camera.Focus(_options.Focus);

        if (_options.CameraDistance > 0) {

            _camera.Rig.Distance = _options.CameraDistance;

        }

    }

    private DebugShapePass CreatePasses() {

        var shaderRoot = Path.Combine(AppContext.BaseDirectory, "Shaders");

        using var shaders = new ShaderLibrary(_vulkan.Device, shaderRoot);

        var shapes = new DebugShapePass(_vulkan.Device, _renderer.Swapchain.RenderPass, shaders, _world, _origin);

        _renderer.AddPass(shapes);
        _renderer.AddPass(new DebugLinePass(_vulkan.Device, _renderer.Swapchain.RenderPass, shaders, _lines));
        _renderer.AddPass(new ImGuiPass(_debugUi.Renderer));

        return shapes;

    }

    private void ResetScenario(Scenario scenario) {

        _warp.Stop();
        _scene.Reset(_world, scenario, _clock.UniversalTime);

    }

    private void CaptureNow() {

        _renderer.Capture.Request(Path.Combine(_options.CaptureDirectory, $"capture-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png"));

    }

    public void Run() {

        var smoke = _options.SmokeFrames > 0;

        var start = Stopwatch.GetTimestamp();
        var last = start;

        var warmupFrames = -1L;
        var allocatedAfterWarmup = 0L;
        var captureFrames = 0;

        while (!_window.ShouldClose) {

            Glfw.PollEvents();

            var now = Stopwatch.GetTimestamp();
            var frameSeconds = Math.Min(Stopwatch.GetElapsedTime(last, now).TotalSeconds, 0.25);

            last = now;

            var capturing = false;

            if (smoke && warmupFrames >= 0 && _options.CaptureEvery > 0) {

                var measured = _stats.FrameCount - warmupFrames;

                if (measured % _options.CaptureEvery == 0 || measured == _options.SmokeFrames - 1) {

                    _renderer.Capture.Request(Path.Combine(_options.CaptureDirectory, $"{_options.Scenario}-{measured:D6}.png"));
                    capturing = true;

                }

            }

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

                    BeginSmokeScript();

                }

                continue;

            }

            // Capture frames copy pixels to managed memory on purpose; keep them out of the hot-path total.
            if (capturing) {

                captureFrames++;

            } else {

                allocatedAfterWarmup += _stats.AllocatedBytesLastFrame;

            }

            if (_stats.FrameCount - warmupFrames >= _options.SmokeFrames) {

                LogSmokeSummary(warmupFrames, allocatedAfterWarmup, captureFrames);
                _window.ShouldClose = true;

            }

        }

    }

    private void BeginSmokeScript() {

        _profile.ResetAllocations();

        if (_options.SmokeThrottle > 0) {

            _world.Get<CraftControl>(_scene.Craft).Throttle = (float)_options.SmokeThrottle;

        }

        var rates = _warp.RailsRates;

        for (var i = 0; i < rates.Length; i++) {

            if (rates[i] <= _options.SmokeWarpRate) {

                _warp.SetLevel(i);

            }

        }

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
        _warp.Update();

        for (var i = 0; i < steps; i++) {

            _systems.Run(SystemPhase.FixedUpdate, _world, _clock.NextFixedStep());

        }

        _systems.Run(SystemPhase.LateUpdate, _world, _clock.FrameTime);

        _predictor.Update(_world.Get<OrbitState>(_scene.Craft), _clock.UniversalTime);
        _camera.Update(_world);
        _origin.Update(_camera.TargetPosition);

        _lines.Clear();
        _orbitDraw.Draw(_clock.UniversalTime);

        var t1 = Stopwatch.GetTimestamp();
        var a1 = GC.GetAllocatedBytesForCurrentThread();

        _debugUi.BuildFrame();

        var t2 = Stopwatch.GetTimestamp();
        var a2 = GC.GetAllocatedBytesForCurrentThread();

        var capturing = _renderer.Capture.IsPending;
        var size = _window.FramebufferSize;
        var aspect = size.Y > 0 ? (float)size.X / size.Y : 1.0f;

        _renderer.Render(size, _camera.Camera.ViewMatrix(_origin), _camera.Camera.ProjectionMatrix(aspect));
        _debugUi.EndFrame();

        var t3 = Stopwatch.GetTimestamp();
        var a3 = GC.GetAllocatedBytesForCurrentThread();

        _profile.SimulationMs = (float)Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds;
        _profile.UiMs = (float)Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds;
        _profile.RenderMs = (float)Stopwatch.GetElapsedTime(t2, t3).TotalMilliseconds;

        if (!capturing) {

            _profile.Accumulate(a1 - a0, a2 - a1, a3 - a2);

        }

        _input.EndFrame();

    }

    private void HandleShortcuts() {

        if (_input.WasPressedRaw(GlfwKey.Escape)) {

            _window.ShouldClose = true;

        }

        if (_input.WasPressedRaw(GlfwKey.F3)) {

            _debugUi.Visible = !_debugUi.Visible;

        }

        if (_input.WasPressedRaw(GlfwKey.F12)) {

            CaptureNow();

        }

        if (_input.WasPressed(GlfwKey.Space)) {

            _clock.Paused = !_clock.Paused;

        }

        if (_input.WasPressed(GlfwKey.Period)) {

            _warp.Increase();

        }

        if (_input.WasPressed(GlfwKey.Comma)) {

            _warp.Decrease();

        }

        if (_input.WasPressed(GlfwKey.Slash)) {

            _warp.Stop();

        }

    }

    private void LogSmokeSummary(long warmupFrames, long allocatedAfterWarmup, int captureFrames) {

        var orbit = _world.Get<OrbitState>(_scene.Craft);
        var body = _bodies[orbit.Body];

        Log.Info($"Smoke: {_stats.FrameCount} frames, avg {_stats.AverageMs:F2} ms ({_stats.Fps:F0} FPS), max {_stats.MaxMs:F2} ms");
        Log.Info($"Smoke: after {warmupFrames} warmup frames ({SmokeWarmupSeconds} s), {_options.SmokeFrames - captureFrames} measured frames allocated {allocatedAfterWarmup} B on the main thread ({captureFrames} capture frames excluded)");
        Log.Info($"Smoke: allocations by section: sim {_profile.SimulationBytes} B, ui {_profile.UiBytes} B, render {_profile.RenderBytes} B");
        Log.Info($"Smoke: UT {_clock.UniversalTime / 86400:F3} d, warp x{_warp.Rate:F0} ({_warp.Mode}), origin shifts {_origin.ShiftCount}");
        Log.Info($"Smoke: craft around {body.Name}, altitude {(orbit.Current.Position.Length() - body.Radius) / 1000:F1} km, SOI transitions {_orbitEvents.TransitionCount}");

        for (var i = _orbitEvents.RecentCount - 1; i >= 0; i--) {

            var e = _orbitEvents.Recent(i);

            Log.Info($"Smoke: transition at UT {e.Time / 86400:F4} d: {_bodies[e.FromBody].Name} -> {_bodies[e.ToBody].Name}");

        }

        Log.Info($"Smoke: {_renderer.FramesRendered} frames presented, {_renderer.Capture.CapturesWritten} captures, {_lines.Count} line vertices ({_lines.Dropped} dropped)");

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
