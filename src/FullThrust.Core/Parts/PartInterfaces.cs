using FullThrust.Core.Ecs;
using FullThrust.Core.Simulation;

namespace FullThrust.Core.Parts;

public interface IPartModule {

    void FixedUpdate(World world, Entity part, in SimTime time);

}

public interface IPartDefinition {

    string Id { get; }

    double DryMass { get; }

    void Attach(World world, Entity part);

}
