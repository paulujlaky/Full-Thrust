using FullThrust.Core.Ecs;
using FullThrust.Core.Parts;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Craft;

// Part entities carry world transforms only for rendering; craft space is the source of truth.
public sealed class PartTransformSystem : ISystem {

    public void Update(World world, in SimTime time) {

        var transforms = world.Store<Transform>();

        foreach (var row in world.Query<Part, Transform>()) {

            ref readonly var part = ref row.C1;
            ref readonly var craft = ref transforms.Get(part.Craft);

            var position = craft.Position + craft.Rotation * part.LocalPosition;
            var rotation = craft.Rotation * part.LocalRotation;

            row.C2 = new Transform(position, rotation);

        }

    }

}
