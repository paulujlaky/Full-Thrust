using Brutal.ImGuiApi;
using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Craft;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Parts;
using FullThrust.Core.Physics;
using FullThrust.Core.Space;

namespace FullThrust.Game.Debug;

public sealed class CraftPanel : IDebugPanel {

    private const double Rad = 180.0 / Math.PI;

    // Indexed by enum value so drawing never calls Enum.ToString.
    private static readonly string[] StatusNames = ["off", "starting", "running", "flameout", "ignition failed"];
    private static readonly string[] FaultNames = ["", "no ignitions left", "no propellant", "propellant unsettled", "low inlet pressure", "pump cavitation", "propellant depleted", "low feed pressure"];
    private static readonly string[] ModeNames = ["rails", "physics"];

    private static readonly float4 Warning = new(1, 0.55f, 0.3f, 1);
    private static readonly float4 Good = new(0.5f, 1, 0.55f, 1);

    private readonly World _world;
    private readonly FlightFocus _focus;
    private readonly CelestialSystem _system;
    private readonly PartCatalog _parts;

    private readonly PhysicsFrame _frame;
    private readonly IRigidBodyWorld _bodies;
    private readonly FlightPipeline _pipeline;

    public CraftPanel(World world, FlightFocus focus, CelestialSystem system, PartCatalog parts, PhysicsFrame frame, IRigidBodyWorld bodies, FlightPipeline pipeline) {

        _world = world;
        _focus = focus;
        _system = system;
        _parts = parts;
        _frame = frame;
        _bodies = bodies;
        _pipeline = pipeline;

    }

    public string Title => "Craft";

    public bool Visible { get; set; } = true;

    public void Draw() {

        ImGui.SetNextWindowPos(new float2(345, 60), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(380, 600), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Craft"u8)) {

            ImGui.End();

            return;

        }

        var craft = _focus.Craft;

        if (_world.IsAlive(craft) && _world.Has<Vessel>(craft)) {

            DrawSummary(craft);
            DrawFlight(craft);
            DrawEngines(craft);
            DrawTanks(craft);

        } else {

            ImGui.TextDisabled("No craft"u8);

        }

        DrawPhysics();

        ImGui.End();

    }

    private void DrawSummary(Entity craft) {

        ref readonly var vessel = ref _world.Get<Vessel>(craft);
        ref readonly var staging = ref _world.Get<CraftStaging>(craft);
        ref readonly var control = ref _world.Get<CraftControl>(craft);
        ref readonly var mass = ref _world.Get<CraftMass>(craft);
        ref readonly var flight = ref _world.Get<FlightState>(craft);

        var weight = mass.Mass * flight.Gravity.Length();
        var com = mass.CenterOfMass;
        var inertia = mass.Inertia.Diagonal;

        ImGui.SeparatorText("Vessel"u8);
        ImGui.Text($"{_world.Get<Name>(craft).Value}: {vessel.PartCount} parts, stage {staging.NextStage}/{staging.StageCount} (Space)");
        ImGui.Text($"Mode {ModeNames[(int)flight.Mode]}  SAS {(control.Sas ? "on" : "off")} (T)  RCS {(control.Rcs ? "on" : "off")} (R)  throttle {control.Throttle * 100:F0}%");

        if (flight.Landed) {

            ImGui.TextColored(Good, "Landed"u8);

        }

        ImGui.Text($"Mass {mass.Mass / 1000:F3} t  thrust {flight.Thrust / 1000:F1} kN  TWR {(weight > 0 ? flight.Thrust / weight : 0):F2}");
        ImGui.Text($"CoM ({com.X:F2}, {com.Y:F2}, {com.Z:F2}) m");
        ImGui.Text($"Inertia ({inertia.X:E3}, {inertia.Y:E3}, {inertia.Z:E3}) kg m2");

    }

    private void DrawFlight(Entity craft) {

        ref readonly var flight = ref _world.Get<FlightState>(craft);
        ref readonly var orbit = ref _world.Get<OrbitState>(craft);

        var up = double3.NormalizeOrZero(orbit.Current.Position);
        var vertical = double3.Dot(orbit.Current.Velocity, up);
        var ambient = flight.Ambient;

        ImGui.SeparatorText("Flight"u8);
        ImGui.Text($"{_system[flight.Body].Name}  altitude {flight.Altitude / 1000:F3} km  vertical {vertical:F1} m/s");
        ImGui.Text($"Surface speed {flight.AirVelocity.Length():F1} m/s  Mach {flight.Mach:F2}  q {flight.DynamicPressure / 1000:F2} kPa");
        ImGui.Text($"Air p {ambient.Pressure:F0} Pa  rho {ambient.Density:F4} kg/m3  T {ambient.Temperature:F1} K");
        ImGui.Text($"Drag {flight.Drag / 1000:F2} kN  accel {flight.ProperAcceleration.Length() / PhysicalConstants.StandardGravity:F2} g  ullage {flight.UllageSettle * 100:F0}%");

    }

    private void DrawEngines(Entity craft) {

        ImGui.SeparatorText("Engines"u8);

        foreach (var row in _world.Query<EngineState, Part>()) {

            ref readonly var engine = ref row.C1;
            ref readonly var part = ref row.C2;

            if (part.Craft != craft) {

                continue;

            }

            var definition = engine.Definition;
            var status = StatusNames[(int)engine.Status];
            var gimbal = engine.Gimbal.Length() * Rad;
            var ignitions = engine.IgnitionsRemaining;

            ImGui.Text($"{part.Definition.Name} [stage {part.Stage}{(engine.Armed ? ", armed" : "")}]");

            if (engine.Fault != EngineFault.None) {

                ImGui.TextColored(Warning, $"   {status}: {FaultNames[(int)engine.Fault]}");

            } else {

                ImGui.Text($"   {status}  pump {engine.Pump * 100:F0}%  ignitions {(ignitions < 0 ? 99 : ignitions)}");

            }

            ImGui.Text($"   {engine.Thrust / 1000:F1} kN  Isp {engine.Isp:F1} s  {engine.MassFlow:F1} kg/s  gimbal {gimbal:F2}/{definition.GimbalRange * Rad:F0} deg");

        }

    }

    private void DrawTanks(Entity craft) {

        if (!ImGui.CollapsingHeader("Tanks"u8, ImGuiTreeNodeFlags.DefaultOpen)) {

            return;

        }

        var resources = _parts.Resources;

        foreach (var row in _world.Query<TankState, Part>()) {

            ref readonly var tank = ref row.C1;
            ref readonly var part = ref row.C2;

            if (part.Craft != craft) {

                continue;

            }

            ImGui.Text($"{part.Definition.Name} (group {part.FeedGroup})  {tank.Pressure / 1000:F0} kPa  He {tank.PressurantMass:F1} kg");

            for (var i = 0; i < resources.Count; i++) {

                var capacity = tank.Definition.Capacity[i];

                if (capacity <= 0) {

                    continue;

                }

                ImGui.Text($"   {resources[i].Name} {tank.Amount[i] / 1000:F2} / {capacity / 1000:F2} t");

            }

        }

        foreach (var row in _world.Query<RcsState, Part>()) {

            if (row.C2.Craft == craft && row.C1.Thrust > 0) {

                ImGui.Text($"RCS {row.C2.Definition.Name}: {row.C1.Thrust:F0} N");

            }

        }

    }

    private void DrawPhysics() {

        if (!ImGui.CollapsingHeader("Physics"u8)) {

            return;

        }

        var origin = _frame.Origin;
        var velocity = _frame.Velocity;

        ImGui.Text($"Rigid bodies {_bodies.BodyCount}  steps {_pipeline.RigidBodies.Steps}  stages fired {_pipeline.Staging.StageCount}");
        ImGui.Text($"Frame origin ({origin.X:F0}, {origin.Y:F0}, {origin.Z:F0})");
        ImGui.Text($"Frame velocity ({velocity.X:F1}, {velocity.Y:F1}, {velocity.Z:F1})  shifts {_frame.ShiftCount}");
        ImGui.Text($"Craft removed by range/impact {_pipeline.Modes.CraftDestroyed}");

    }

}
