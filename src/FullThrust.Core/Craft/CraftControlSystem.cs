using Brutal.Numerics;
using FullThrust.Core.Ecs;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Craft;

public sealed class CraftControlSystem : ISystem {

    public void Update(World world, in SimTime time) {

        var dt = time.DeltaTime;

        foreach (var row in world.Query<CraftControl, ControllableCraft, Velocity>()) {

            ref var control = ref row.C1;
            ref var craft = ref row.C2;
            ref var velocity = ref row.C3;

            ref var transform = ref world.Get<Transform>(row.Entity);

            // Craft nose is body +Y, matching a vertically stacked rocket.
            var nose = transform.Rotation * double3.UnitY;
            var thrust = nose * (control.Throttle * craft.MaxThrust);

            var translate = transform.Rotation * new double3(control.Translation.X, control.Translation.Y, control.Translation.Z);

            velocity.Linear += (thrust + translate * craft.MaxThrust * 0.1) / craft.Mass * dt;

            var bodyInput = new double3(control.Pitch, control.Roll, control.Yaw);
            var worldInput = transform.Rotation * bodyInput;

            velocity.Angular += worldInput * (craft.MaxAngularAcceleration * dt);
            velocity.Angular *= Math.Max(0.0, 1.0 - craft.AngularDamping * dt);

        }

    }

}
