using FullThrust.Core.Ecs;
using FullThrust.Core.Physics;
using FullThrust.Core.Simulation;

namespace FullThrust.Core.Craft;

// Recomputes mass, centre of mass and inertia from dry parts plus remaining propellant, then pushes them to physics.
public sealed class CraftMassSystem : ISystem {

    private readonly IRigidBodyWorld _bodies;

    private readonly ShapeElement[] _shape = new ShapeElement[CraftAssembly.MaxPartsPerCraft];

    public CraftMassSystem(IRigidBodyWorld bodies) {

        _bodies = bodies;

    }

    public void Update(World world, in SimTime time) {

        var physics = world.Store<PhysicsBody>();

        foreach (var row in world.Query<Vessel, CraftMass>()) {

            ref var vessel = ref row.C1;
            ref var mass = ref row.C2;

            mass = CraftAssembly.ComputeMass(world, row.Entity);

            ref readonly var body = ref physics.TryGet(row.Entity, out var hasBody);

            if (!hasBody) {

                continue;

            }

            if (vessel.ShapeDirty) {

                var count = CraftAssembly.CollectShape(world, row.Entity, _shape);

                _bodies.SetShape(body.Handle, new ReadOnlySpan<ShapeElement>(_shape, 0, count), mass.CenterOfMass);
                vessel.ShapeDirty = false;

            }

            _bodies.SetMass(body.Handle, mass.Distribution);

        }

    }

}
