using System.Diagnostics;
using System.Globalization;
using Brutal.GlfwApi;
using Brutal.Logging;
using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Craft;
using FullThrust.Core.Diagnostics;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Parts;
using FullThrust.Core.Physics;
using FullThrust.Core.Rendering;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;
using FullThrust.Game.Controls;
using FullThrust.Game.Debug;
using FullThrust.Game.Platform;
using FullThrust.Game.Rendering;
using FullThrust.Game.Rendering.Volumetrics;
using FullThrust.Game.Scene;
using FullThrust.Game.Terrain;
using FullThrust.Physics.Jolt;

namespace FullThrust.Game;

public sealed class GameOptions {

    public int2 WindowSize = new(1600, 900);

    public bool Validation = true;

    public bool VSync = true;

    public bool Overlay = true;

    public string SystemFile = Path.Combine("data", "celestial", "earth-moon.json");

    public string PartsFile = Path.Combine("data", "parts", "parts.json");

    public string RocketsFile = Path.Combine("data", "rockets", "rockets.json");

    public string ModelsDirectory = Path.Combine("data", "models");

    public Scenario Scenario = Scenario.Launchpad;

    public string? Rocket;

    public int Focus;

    public double CameraDistance;

    public double CameraPitch = double.NaN;

    public double CameraYaw = double.NaN;

    public bool Headlight;

    public bool ShowTerrainLevels;

    // NaN keeps the default daylight exposure.
    public double Ev100 = double.NaN;

    // "azimuth,elevation" in degrees relative to the local horizon.
    public string? Sun;

    // Puts a ball of smoke beside the camera target.
    public bool DebugVolume;

    // "latitude,longitude,altitude" pins the camera above that place; --camera-yaw and --camera-pitch aim it.
    public string? CameraSite;

    // Replaces the shaded scene with a debug view for captures: "ao" or "cascades".
    public string? DebugView;

    public double TimeScale = 1;

    public string CaptureDirectory = "captures";

    // Non-zero runs a scripted smoke test: warm up, measure this many frames, log a summary and exit.
    public int SmokeFrames;

    public double SmokeWarpRate = 1;

    public double SmokeThrottle;

    public int SmokeStages;

    public bool SmokeAutoStage;

    public bool SmokeSas;

    public float3 SmokeTranslation;

    public int CaptureEvery;

}

public sealed class GameApp : IDisposable {

    private static readonly LogCategory Log = LogCategory.Make("Game");

    private const double SmokeWarmupSeconds = 3.0;

    // Covers bodies, several multi-stage craft and their debris without growing ECS storage mid-flight.
    private const int ReservedEntities = 1024;

    // Sim seconds to wait after a stage before the smoke script may stage again.
    private const double AutoStageCooldown = 2.0;

    // Metres the orbit camera keeps above the ground.
    private const double CameraClearance = 2.0;

    private static readonly double[] StageCaptureDelays = [0.5, 2.0];

    private readonly GameOptions _options;

    private readonly GlfwWindow _window;
    private readonly InputState _input;

    private readonly VulkanContext _vulkan;
    private readonly Renderer _renderer;
    private readonly SceneLighting _lighting = new();
    private readonly DebugUi _debugUi;
    private readonly GpuUploader _uploader;
    private ModelLibrary? _modelLibrary;
    private TerrainRenderer? _terrain;
    private MeshPass? _meshPass;
    private AmbientOcclusion? _occlusion;
    private BackgroundPass? _background;
    private AtmosphereRenderer? _atmosphere;
    private VolumetricRenderer? _volumetrics;
    private PlumeSource? _plumes;
    private CloudSource? _clouds;
    private ExposureController? _exposure;
    private BloomPass? _bloom;

    private readonly World _world = new();
    private readonly SystemScheduler _systems = new();
    private readonly TimeWarpController _warp = new();
    private readonly SimulationClock _clock;
    private readonly FloatingOrigin _origin = new();

    private readonly CelestialSystem _bodies;
    private readonly OrbitEvents _orbitEvents = new();
    private readonly TrajectoryPredictor _predictor;

    private readonly ModelCatalog _models = new();
    private readonly PartCatalog _parts;
    private readonly RocketCatalog _rockets;

    private readonly JoltRigidBodyWorld _physics;
    private readonly PhysicsFrame _physicsFrame = new();
    private readonly FlightFocus _focus = new();
    private readonly FlightPipeline _flight;

    private readonly DemoScene _scene;
    private readonly CameraController _camera;
    private readonly FlightInputSystem _flightInput;

    private readonly DebugLines _lines;
    private readonly OrbitDebugDraw _orbitDraw;
    private readonly CraftDebugDraw _craftDraw;

    private readonly FrameStats _stats = new();
    private readonly FrameProfile _profile = new();

    private int _pendingStages;
    private double _lastStageTime = double.NegativeInfinity;
    private int _stageCaptures;
    private bool _profileAfterCapture;

    // Latitude and longitude in degrees and metres above the ground, from --camera-site.
    private readonly double3? _site;

    public GameApp(GameOptions options) {

        _options = options;

        if (options.CameraSite?.Split(',') is [var latitude, var longitude, var height]) {

            _site = new double3(double.Parse(latitude, CultureInfo.InvariantCulture), double.Parse(longitude, CultureInfo.InvariantCulture), double.Parse(height, CultureInfo.InvariantCulture));

        }

        _clock = new SimulationClock(_warp) { TimeScale = options.TimeScale };
        _world.Reserve(ReservedEntities);

        var root = AppContext.BaseDirectory;

        _bodies = CelestialSystemLoader.LoadFile(Path.Combine(root, options.SystemFile), new KeplerPropagator());
        _predictor = new TrajectoryPredictor(_bodies, new PatchedConicSolver(_bodies));

        _parts = PartCatalogLoader.LoadFile(Path.Combine(root, options.PartsFile), _models);
        _rockets = RocketCatalogLoader.LoadFile(Path.Combine(root, options.RocketsFile), _parts);

        var rocket = options.Rocket is null ? _rockets.Rockets[0] : _rockets.Find(options.Rocket) ?? throw new ArgumentException($"Unknown rocket '{options.Rocket}'.");

        _physics = new JoltRigidBodyWorld();
        _flight = new FlightPipeline(_bodies, _physics, _physicsFrame, _focus, _orbitEvents);

        if (!Glfw.Init()) {

            throw new InvalidOperationException("Failed to initialise GLFW.");

        }

        Glfw.WindowHint(GlfwWindowHint.ClientApi, Glfw.Constants.GLFW_NO_API);
        Glfw.WindowHint(GlfwWindowHint.Resizable, Glfw.Constants.GLFW_TRUE);

        _window = Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = "Full Thrust", Size = options.WindowSize });
        _input = new InputState(_window);

        _vulkan = new VulkanContext(_window, options.Validation);
        _lighting.Headlight = options.Headlight;

        if (options.Sun?.Split(',') is [var azimuth, var elevation]) {

            _lighting.SunAzimuthElevation = new float2(float.Parse(azimuth, CultureInfo.InvariantCulture), float.Parse(elevation, CultureInfo.InvariantCulture));

        }

        if (!double.IsNaN(options.Ev100)) {

            _lighting.Ev100 = (float)options.Ev100;

        }

        _renderer = new Renderer(_vulkan, _window.FramebufferSize, options.VSync, _lighting);
        _debugUi = new DebugUi(_window, _vulkan, _renderer.Swapchain) { Visible = options.Overlay };
        _uploader = new GpuUploader(_vulkan);

        _scene = new DemoScene(_bodies, _rockets, _focus, _models);
        _scene.Populate(_world, options.Scenario, rocket, _clock.UniversalTime);

        _camera = new CameraController(_input, new Camera(), _scene);
        _flightInput = new FlightInputSystem(_input, _focus);

        AddCameraTargets();

        _warp.AddConstraint(new ThrustWarpConstraint(_world));
        _warp.AddConstraint(new PhysicsWarpConstraint(_world));

        _systems.Add(SystemPhase.Update, _flightInput);

        _flight.AddFixedSystems(_systems)
            .Add(SystemPhase.LateUpdate, new CelestialBodySystem(_bodies))
            .Add(SystemPhase.LateUpdate, new SurfaceAnchorSystem(_bodies))
            .Add(SystemPhase.LateUpdate, new OrbitPropagationSystem(_bodies, new PatchedConicSolver(_bodies), _orbitEvents))
            .Add(SystemPhase.LateUpdate, _flight.PartTransforms);

        _lines = new DebugLines(_origin);
        _orbitDraw = new OrbitDebugDraw(_bodies, _predictor, _lines);
        _craftDraw = new CraftDebugDraw(_world, _lines);

        var shapes = CreatePasses();

        _debugUi.AddPanel(new FrameTimingPanel(_stats, _profile, _renderer, _vulkan));
        _debugUi.AddPanel(new SimulationPanel(_world, _clock, _origin, _stats, _scene, shapes));
        _debugUi.AddPanel(new TweaksPanel(_clock, _origin, _renderer, _camera, _flightInput));
        _debugUi.AddPanel(new OrbitPanel(_world, _bodies, _clock, _scene, _predictor, _orbitEvents));
        _debugUi.AddPanel(new TimeWarpPanel(_warp, _clock));
        _debugUi.AddPanel(new ScenarioPanel(ResetScenario, _scene, _orbitDraw, _craftDraw, _camera, CaptureNow));
        _debugUi.AddPanel(new CraftPanel(_world, _focus, _bodies, _parts, _physicsFrame, _physics, _flight));
        _debugUi.AddPanel(new RenderingPanel(_terrain!, _meshPass!, _modelLibrary!, _lighting, _renderer.Shadows, _occlusion!, _background!, _atmosphere!, _exposure!, _bloom!));
        _debugUi.AddPanel(new ControlsPanel());

        _origin.ShiftTo(_world.Get<Transform>(_scene.Craft).Position);

    }

    private void AddCameraTargets() {

        _camera.AddTarget(new CameraTarget { Label = "Craft", Entity = _scene.Craft, DefaultDistance = CraftDistance(_scene.Rocket), MinDistance = 2 });

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

        if (!double.IsNaN(_options.CameraPitch)) {

            _camera.Rig.Pitch = _options.CameraPitch * Math.PI / 180;

        }

        if (!double.IsNaN(_options.CameraYaw)) {

            _camera.Rig.Yaw = _options.CameraYaw * Math.PI / 180;

        }

    }

    private static double CraftDistance(RocketPreset rocket) => Math.Max(25, rocket.Height * 1.6);

    private DebugShapePass CreatePasses() {

        var shaderRoot = Path.Combine(AppContext.BaseDirectory, "Shaders");

        using var shaders = new ShaderLibrary(_vulkan.Device, shaderRoot);

        var shapes = new DebugShapePass(_vulkan.Device, _renderer.SceneRenderPass, _renderer.Frames.SetLayout, shaders, _world, _origin);

        _modelLibrary = new ModelLibrary(_vulkan, _uploader, _models, Path.Combine(AppContext.BaseDirectory, _options.ModelsDirectory));

        _terrain = new TerrainRenderer(_vulkan.Device, _uploader, _renderer.SceneRenderPass, _renderer.DepthRenderPass, _renderer.Frames.SetLayout, shaders, _bodies, _origin, _vulkan.SamplerAnisotropy, Path.Combine(AppContext.BaseDirectory, "data", "textures", "terrain"));

        _terrain.ShowLevels = _options.ShowTerrainLevels;
        _renderer.AddPass(_terrain);
        _meshPass = new MeshPass(_vulkan.Device, _renderer.SceneRenderPass, _renderer.DepthRenderPass, _renderer.Frames.SetLayout, shaders, _world, _origin, _modelLibrary);

        _renderer.AddPass(_meshPass);
        _background = new BackgroundPass(_vulkan.Device, _renderer.SceneRenderPass, _renderer.Frames.SetLayout, shaders);
        _renderer.AddPass(_background);
        _atmosphere = new AtmosphereRenderer(_vulkan.Device, shaders, _renderer.Frames, _bodies, _origin);
        _renderer.AddPass(_atmosphere);

        _volumetrics = new VolumetricRenderer(_vulkan.Device, shaders, _renderer.Hdr, _renderer.Frames, _origin);
        _renderer.AddPass(_volumetrics);
        _renderer.AddPass(_volumetrics.CompositePass);

        _plumes = new PlumeSource(_world, _origin, _parts, Path.Combine(AppContext.BaseDirectory, "data", "effects", "plumes.json"));
        _volumetrics.AddSource(_plumes);

        _clouds = new CloudSource(_bodies, _origin);
        _volumetrics.AddSource(_clouds);

        if (_options.DebugVolume) {

            _volumetrics.AddSource(new DebugVolumeSource(() => _camera.TargetPosition, () => _clock.UniversalTime, _origin));

        }

        _occlusion = new AmbientOcclusion(_vulkan.Device, shaders, _renderer.Hdr, _renderer.Frames) { ShowOcclusion = _options.DebugView == "ao" };
        _renderer.Shadows.ShowCascades = _options.DebugView == "cascades";
        _renderer.AddPass(_occlusion);

        _renderer.Prepass.AddCaster(_terrain);
        _renderer.Prepass.AddCaster(_meshPass);
        _renderer.Shadows.AddCaster(_terrain);
        _renderer.Shadows.AddCaster(_meshPass);
        _renderer.AddPass(shapes);
        _renderer.AddPass(new DebugLinePass(_vulkan.Device, _renderer.SceneRenderPass, shaders, _lines));
        _exposure = new ExposureController(_vulkan.Device, shaders, _renderer.Hdr, _lighting) { Automatic = double.IsNaN(_options.Ev100) };
        _bloom = new BloomPass(_vulkan.Device, shaders, _renderer.Hdr);

        _renderer.AddPass(_exposure);
        _renderer.AddPass(_bloom);
        _renderer.AddPass(new TonemapPass(_vulkan.Device, _renderer.Swapchain.RenderPass, shaders, _renderer.Hdr, _lighting, _bloom));
        _renderer.AddPass(new ImGuiPass(_debugUi.Renderer));

        return shapes;

    }

    private void ResetScenario(Scenario scenario, RocketPreset rocket) {

        _warp.Stop();
        _flight.Modes.DestroyAll(_world);

        var craft = _scene.Spawn(_world, scenario, rocket, _clock.UniversalTime);

        _predictor.Reset();
        _camera.Retarget(0, craft, CraftDistance(rocket), 2);
        _origin.ShiftTo(_world.Get<Transform>(craft).Position);

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
        var afterCapture = false;

        while (!_window.ShouldClose) {

            Glfw.PollEvents();

            var now = Stopwatch.GetTimestamp();
            var frameSeconds = Math.Min(Stopwatch.GetElapsedTime(last, now).TotalSeconds, 0.25);

            last = now;

            var capturing = false;

            if (smoke && warmupFrames >= 0) {

                DriveSmokeScript();
                CaptureAfterStaging();

                capturing = _renderer.Capture.IsPending;

            }

            if (smoke && warmupFrames >= 0 && _options.CaptureEvery > 0) {

                var measured = _stats.FrameCount - warmupFrames;

                if (measured % _options.CaptureEvery == 0 || measured == _options.SmokeFrames - 1) {

                    _renderer.Capture.Request(Path.Combine(_options.CaptureDirectory, $"{_scene.Rocket.Id}-{_options.Scenario}-{measured:D6}.png"));
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
                    _renderer.Profiler.ResetAverages();
                    _profile.ResetAllocations();

                    BeginSmokeScript();

                }

                continue;

            }

            // Captures copy pixels to managed memory on purpose (and spill into the next frame); keep them out of the total.
            var excluded = capturing || afterCapture;

            afterCapture = capturing;

            if (excluded) {

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

        ref var control = ref _world.Get<CraftControl>(_scene.Craft);

        control.Sas = _options.SmokeSas;
        _flightInput.HeldTranslation = _options.SmokeTranslation;

        if (_options.SmokeThrottle > 0) {

            control.Throttle = (float)_options.SmokeThrottle;

        }

        _pendingStages = _options.SmokeStages;

        var rates = _warp.RailsRates;

        for (var i = 0; i < rates.Length; i++) {

            if (rates[i] <= _options.SmokeWarpRate) {

                _warp.SetLevel(i);

            }

        }

    }

    // Issues one stage request at a time, and with auto-staging stages again once every lit engine has burned out.
    private void DriveSmokeScript() {

        var craft = _scene.Craft;

        if (!_world.IsAlive(craft)) {

            return;

        }

        ref var staging = ref _world.Get<CraftStaging>(craft);

        if (staging.Requested || _clock.UniversalTime - _lastStageTime < AutoStageCooldown) {

            return;

        }

        var stage = _pendingStages > 0;

        if (!stage && _options.SmokeAutoStage && staging.NextStage < staging.StageCount) {

            stage = !AnyEngineBurning(craft);

        }

        if (!stage) {

            return;

        }

        staging.Requested = true;
        _lastStageTime = _clock.UniversalTime;
        _pendingStages = Math.Max(0, _pendingStages - 1);
        _stageCaptures = 0;

    }

    // With captures enabled, also grab frames shortly after each stage so separations are on record.
    private void CaptureAfterStaging() {

        if (_options.CaptureEvery <= 0 || _stageCaptures >= StageCaptureDelays.Length || double.IsNegativeInfinity(_lastStageTime)) {

            return;

        }

        if (_clock.UniversalTime - _lastStageTime < StageCaptureDelays[_stageCaptures]) {

            return;

        }

        var stage = _world.IsAlive(_scene.Craft) ? _world.Get<CraftStaging>(_scene.Craft).NextStage : 0;

        _renderer.Capture.Request(Path.Combine(_options.CaptureDirectory, $"{_scene.Rocket.Id}-{_options.Scenario}-stage{stage}-{_stageCaptures}.png"));
        _stageCaptures++;

    }

    private bool AnyEngineBurning(Entity craft) {

        foreach (var row in _world.Query<EngineState, Part>()) {

            if (row.C2.Craft == craft && row.C1.IsBurning) {

                return true;

            }

        }

        return false;

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

        if (_world.IsAlive(_scene.Craft)) {

            _predictor.Update(_world.Get<OrbitState>(_scene.Craft), _clock.UniversalTime);

        }

        _camera.Update(_world);
        PlaceCamera();
        _origin.Update(_site is null ? _camera.TargetPosition : _camera.Camera.Position);

        _lines.Clear();
        _orbitDraw.Draw(_clock.UniversalTime);
        _craftDraw.Draw();

        var t1 = Stopwatch.GetTimestamp();
        var a1 = GC.GetAllocatedBytesForCurrentThread();

        _debugUi.BuildFrame();

        var t2 = Stopwatch.GetTimestamp();
        var a2 = GC.GetAllocatedBytesForCurrentThread();

        var capturing = _renderer.Capture.IsPending;
        var excluded = capturing || _profileAfterCapture;

        _profileAfterCapture = capturing;

        var size = _window.FramebufferSize;
        var aspect = size.Y > 0 ? (float)size.X / size.Y : 1.0f;

        _lighting.Update(_bodies, _clock.UniversalTime, _camera.Camera.Position, _camera.Camera.Forward, _camera.Camera.Up);
        _renderer.Seconds = _clock.UniversalTime;
        _terrain?.Prepare(_camera.Camera.Position, _clock.UniversalTime);
        _atmosphere?.Update(_camera.Camera.Position, _clock.UniversalTime);
        _clouds?.Update(_camera.Camera.Position, _clock.UniversalTime);
        _renderer.Render(size, _camera.Camera.ViewMatrix(_origin), _camera.Camera.ProjectionMatrix(aspect));
        _debugUi.EndFrame();

        var t3 = Stopwatch.GetTimestamp();
        var a3 = GC.GetAllocatedBytesForCurrentThread();

        _profile.SimulationMs = (float)Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds;
        _profile.UiMs = (float)Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds;
        _profile.RenderMs = (float)Stopwatch.GetElapsedTime(t2, t3).TotalMilliseconds;

        if (!excluded) {

            _profile.Accumulate(a1 - a0, a2 - a1, a3 - a2);

        }

        _input.EndFrame();

    }

    // The debug site pins the camera above a place on the ground; otherwise the orbit camera is kept out of the terrain.
    private void PlaceCamera() {

        var camera = _camera.Camera;
        var time = _clock.UniversalTime;
        var body = _bodies.DominantBodyAt(_camera.TargetPosition, time);
        var center = _bodies.StateAt(body.Index, time).Position;

        if (_site is { } site) {

            var direction = CelestialSystem.BodyFixedPoint(body, site.X * Math.PI / 180, site.Y * Math.PI / 180, 0) / body.Radius;
            var ground = body.Terrain is { } terrain ? Math.Max(terrain.Height(direction), terrain.HasOcean ? 0 : double.MinValue) : 0;

            camera.Position = _bodies.BodyFixedToWorld(body.Index, direction * (body.Radius + ground + site.Z), time).Position;

            var up = double3.Normalize(camera.Position - center);
            var axis = body.RotationAt(time) * double3.UnitY;
            var east = double3.Normalize(double3.Cross(axis, up));
            var north = double3.Cross(up, east);
            var yaw = double.IsNaN(_options.CameraYaw) ? 0 : _options.CameraYaw * Math.PI / 180;
            var pitch = double.IsNaN(_options.CameraPitch) ? 0 : _options.CameraPitch * Math.PI / 180;
            var forward = (north * Math.Cos(yaw) + east * Math.Sin(yaw)) * Math.Cos(pitch) + up * Math.Sin(pitch);

            camera.Rotation = OrbitCameraRig.LookRotation(forward, up);

            return;

        }

        var altitude = _bodies.SurfaceAltitude(body.Index, camera.Position, time);

        if (altitude < CameraClearance) {

            var radial = double3.Normalize(camera.Position - center);

            camera.Position += radial * (CameraClearance - altitude);
            camera.Rotation = OrbitCameraRig.LookRotation(double3.Normalize(_camera.TargetPosition - camera.Position), radial);

        }

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

        if (_input.WasPressed(GlfwKey.P)) {

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

        Log.Info($"Smoke: {_stats.FrameCount} frames, avg {_stats.AverageMs:F2} ms ({_stats.Fps:F0} FPS), max {_stats.MaxMs:F2} ms");
        Log.Info($"Smoke: after {warmupFrames} warmup frames ({SmokeWarmupSeconds} s), {_options.SmokeFrames - captureFrames} measured frames allocated {allocatedAfterWarmup} B on the main thread ({captureFrames} capture and post-capture frames excluded)");
        Log.Info($"Smoke: allocations by section: sim {_profile.SimulationBytes} B, ui {_profile.UiBytes} B, render {_profile.RenderBytes} B");
        Log.Info($"Smoke: UT {_clock.UniversalTime / 86400:F3} d, warp x{_warp.Rate:F0} ({_warp.Mode}), time scale x{_clock.TimeScale:F1}, origin shifts {_origin.ShiftCount}");

        if (_world.IsAlive(_scene.Craft)) {

            LogCraft(_scene.Craft);

        }

        Log.Info($"Smoke: ground patch rebuilds {_flight.Ground.Rebuilds}, last {_flight.Ground.LastRebuildMilliseconds:F1} ms, terrain chunks built {_terrain?.ChunksBuilt}");
        Log.Info($"Smoke: rigid bodies {_physics.BodyCount}, physics steps {_flight.RigidBodies.Steps}, frame shifts {_physicsFrame.ShiftCount}, stages fired {_flight.Staging.StageCount}, craft removed {_flight.Modes.CraftDestroyed}, SOI transitions {_orbitEvents.TransitionCount}");

        for (var i = _orbitEvents.RecentCount - 1; i >= 0; i--) {

            var e = _orbitEvents.Recent(i);

            Log.Info($"Smoke: transition at UT {e.Time / 86400:F4} d: {_bodies[e.FromBody].Name} -> {_bodies[e.ToBody].Name}");

        }

        var profiler = _renderer.Profiler;
        var extent = _renderer.Swapchain.Extent;

        Log.Info($"Smoke: GPU {extent.Width}x{extent.Height}, frame {profiler.AverageFrameMs:F2} ms");

        for (var i = 0; i < _renderer.Passes.Count && i < GpuProfiler.MaxScopes; i++) {

            if (profiler.AverageScopeMs(i) >= 0.01) {

                Log.Info($"Smoke: GPU   {_renderer.Passes[i].Name}: {profiler.AverageScopeMs(i):F3} ms");

            }

        }

        Log.Info($"Smoke: exposure EV100 {_lighting.Ev100:F2}, measured {_exposure?.MeasuredLuminance:G3} cd/m2");
        Log.Info($"Smoke: {_renderer.FramesRendered} frames presented, {_renderer.Capture.CapturesWritten} captures, {_lines.Count} line vertices ({_lines.Dropped} dropped)");

    }

    private void LogCraft(Entity craft) {

        var orbit = _world.Get<OrbitState>(craft);
        var flight = _world.Get<FlightState>(craft);
        var mass = _world.Get<CraftMass>(craft);
        var staging = _world.Get<CraftStaging>(craft);
        var body = _bodies[orbit.Body];
        var elements = OrbitalElements.FromState(orbit.Current, body.GravitationalParameter);

        Log.Info($"Smoke: {_scene.Rocket.Name} ({_scene.Scenario}) around {body.Name}, mode {flight.Mode}, stage {staging.NextStage}/{staging.StageCount}, mass {mass.Mass / 1000:F2} t");
        Log.Info($"Smoke: altitude {flight.Altitude / 1000:F2} km, surface speed {flight.AirVelocity.Length():F1} m/s, inertial speed {orbit.Current.Velocity.Length():F1} m/s, Mach {flight.Mach:F2}, q {flight.DynamicPressure / 1000:F2} kPa, apoapsis {(elements.Apoapsis - body.Radius) / 1000:F1} km");

        foreach (var row in _world.Query<EngineState, Part>()) {

            if (row.C2.Craft != craft) {

                continue;

            }

            var engine = row.C1;

            Log.Info($"Smoke: engine {row.C2.Definition.Name}: {engine.Status} {engine.Fault}, thrust {engine.Thrust / 1000:F1} kN, Isp {engine.Isp:F1} s, ignitions left {engine.IgnitionsRemaining}");

        }

    }

    public void Dispose() {

        _flight.Modes.DestroyAll(_world);
        _physics.Dispose();

        _renderer.Dispose();
        _modelLibrary?.Dispose();
        _uploader.Dispose();
        _debugUi.Dispose();
        _vulkan.Dispose();

        _input.Dispose();
        _window.Dispose();

        Glfw.Terminate();

    }

}
