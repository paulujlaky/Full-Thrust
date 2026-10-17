using Brutal.Numerics;
using FullThrust.Core.Craft;
using FullThrust.Core.Ecs;
using FullThrust.Core.Parts;
using FullThrust.Core.Space;
using FullThrust.Game.Rendering;

namespace FullThrust.Game.Debug;

// Placeholder visuals until Sprint 5 plumes: exhaust lines scaled by thrust, RCS puffs and the airflow vector.
public sealed class CraftDebugDraw {

    private static readonly float4 Exhaust = new(1.0f, 0.62f, 0.2f, 1);
    private static readonly float4 Puff = new(0.9f, 0.95f, 1.0f, 1);
    private static readonly float4 Airflow = new(0.35f, 0.85f, 1.0f, 1);

    private readonly World _world;
    private readonly DebugLines _lines;

    public CraftDebugDraw(World world, DebugLines lines) {

        _world = world;
        _lines = lines;

    }

    public bool Enabled { get; set; } = true;

    public void Draw() {

        if (!Enabled) {

            return;

        }

        var transforms = _world.Store<Transform>();

        foreach (var row in _world.Query<EngineState, Part>()) {

            ref readonly var engine = ref row.C1;
            ref readonly var part = ref row.C2;

            if (engine.Thrust <= 0) {

                continue;

            }

            ref readonly var craft = ref transforms.Get(part.Craft);

            var definition = part.Definition;
            var exit = craft.Position + craft.Rotation * (part.LocalPosition - part.LocalRotation * new double3(0, definition.Length / 2, 0));
            var direction = craft.Rotation * engine.ThrustDirection;
            var length = definition.Length * (1 + 3 * engine.Thrust / engine.Definition.VacuumThrust);

            // A fan of lines reads as a plume even from far away.
            for (var i = 0; i < 6; i++) {

                var angle = i * Math.PI / 3;
                var side = craft.Rotation * new double3(Math.Cos(angle), 0, Math.Sin(angle)) * (definition.Radius * 0.6);

                _lines.Line(exit + side * 0.5, exit - direction * length + side, Exhaust);

            }

        }

        foreach (var row in _world.Query<RcsState, Part>()) {

            if (row.C1.Thrust <= 0) {

                continue;

            }

            var position = transforms.Get(row.Entity).Position;
            var up = transforms.Get(row.Entity).Rotation * double3.UnitX;

            _lines.Line(position, position + up * 1.5, Puff);

        }

        foreach (var row in _world.Query<FlightState, Transform>()) {

            ref readonly var flight = ref row.C1;

            if (flight.Mode != FlightMode.Physics || flight.DynamicPressure < 1) {

                continue;

            }

            var position = row.C2.Position;

            _lines.Line(position, position - double3.NormalizeOrZero(flight.AirVelocity) * 20, Airflow);

        }

    }

}
