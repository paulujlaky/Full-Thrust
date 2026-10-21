using Brutal.Numerics;
using FullThrust.Core.Craft;
using FullThrust.Core.Ecs;
using FullThrust.Core.Space;
using FullThrust.Game.Rendering;

namespace FullThrust.Game.Debug;

// The airflow vector; exhaust and RCS puffs are drawn by PlumeSource.
public sealed class CraftDebugDraw {

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
