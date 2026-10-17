using Brutal.Numerics;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Craft;

public sealed class CraftControlSystem : ISystem {

    public void Update(World world, in SimTime time) {

        var dt = time.DeltaTime;
        var transforms = world.Store<Transform>();
        var thrusts = world.Store<ThrustAcceleration>();

        foreach (var row in world.Query<CraftControl, ControllableCraft, Velocity>()) {

            ref var control = ref row.C1;
            ref var craft = ref row.C2;
            ref var velocity = ref row.C3;

            ref var transform = ref transforms.Get(row.Entity);

            // Craft nose is body +Y, matching a vertically stacked rocket.
            var nose = transform.Rotation * double3.UnitY;
            var translate = transform.Rotation * new double3(control.Translation.X, control.Translation.Y, control.Translation.Z);

            var force = nose * (control.Throttle * craft.MaxThrust) + translate * (craft.MaxThrust * 0.1);

            ref var thrust = ref thrusts.TryGet(row.Entity, out var hasThrust);

            if (hasThrust) {

                thrust.Linear = force / craft.Mass;

            }

            var bodyInput = new double3(control.Pitch, control.Roll, control.Yaw);
            var worldInput = transform.Rotation * bodyInput;

            velocity.Angular += worldInput * (craft.MaxAngularAcceleration * dt);
            velocity.Angular *= Math.Max(0.0, 1.0 - craft.AngularDamping * dt);

        }

    }

}
