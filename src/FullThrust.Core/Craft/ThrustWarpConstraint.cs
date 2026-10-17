using FullThrust.Core.Ecs;
using FullThrust.Core.Simulation;

namespace FullThrust.Core.Craft;

public sealed class ThrustWarpConstraint : IWarpConstraint {

    private const string Reason = "Cannot warp on rails while thrust is applied";

    private readonly World _world;

    public ThrustWarpConstraint(World world) {

        _world = world;

    }

    public bool Allows(TimeWarpMode mode, double rate, out string reason) {

        reason = Reason;

        if (mode != TimeWarpMode.Rails) {

            return true;

        }

        var flights = _world.Store<FlightState>();

        foreach (var row in _world.Query<CraftControl>()) {

            ref readonly var control = ref row.C1;

            if (control.Throttle > 0 || control.HasTranslationInput) {

                return false;

            }

            ref readonly var flight = ref flights.TryGet(row.Entity, out var hasFlight);

            if (hasFlight && flight.Thrust > 0) {

                return false;

            }

        }

        return true;

    }

}

// Rails warp would skip drag, contact and separation, so it waits until every craft is back on its conic.
public sealed class PhysicsWarpConstraint : IWarpConstraint {

    private const string Reason = "Cannot warp on rails in atmosphere, near the surface or during separation";

    private readonly World _world;

    public PhysicsWarpConstraint(World world) {

        _world = world;

    }

    public bool Allows(TimeWarpMode mode, double rate, out string reason) {

        reason = Reason;

        if (mode != TimeWarpMode.Rails) {

            return true;

        }

        foreach (var row in _world.Query<FlightState>()) {

            if (row.C1.Mode == FlightMode.Physics) {

                return false;

            }

        }

        return true;

    }

}
