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

        foreach (var row in _world.Query<CraftControl>()) {

            ref readonly var control = ref row.C1;

            if (control.Throttle > 0 || control.Translation.X != 0 || control.Translation.Y != 0 || control.Translation.Z != 0) {

                return false;

            }

        }

        return true;

    }

}
