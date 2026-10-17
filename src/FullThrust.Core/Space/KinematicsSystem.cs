using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Simulation;

namespace FullThrust.Core.Space;

public sealed class KinematicsSystem : ISystem {

    public void Update(World world, in SimTime time) {

        var dt = time.DeltaTime;
        var orbits = world.Store<OrbitState>();

        foreach (var row in world.Query<Transform, Velocity>()) {

            ref var transform = ref row.C1;
            ref readonly var velocity = ref row.C2;

            // Orbiting entities get their position from the conic instead.
            if (!orbits.Has(row.Entity)) {

                transform.Position += velocity.Linear * dt;

            }

            transform.Rotation = SpaceMath.Integrate(transform.Rotation, velocity.Angular, dt);

        }

    }

}
