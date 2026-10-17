using Brutal.Numerics;
using FullThrust.Core.Ecs;
using FullThrust.Core.Rendering;

namespace FullThrust.Core.Parts;

public sealed class PartDefinition : IPartDefinition {

    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Category { get; init; }

    public required double DryMass { get; init; }

    public required double Length { get; init; }

    public required double Radius { get; init; }

    public required ShapeKind Shape { get; init; }

    public required float4 Color { get; init; }

    public required double DragCoefficient { get; init; }

    public required IPartModuleDefinition[] Modules { get; init; }

    public double FrontalArea => Math.PI * Radius * Radius;

    public T? Module<T>() where T : class, IPartModuleDefinition {

        foreach (var module in Modules) {

            if (module is T match) {

                return match;

            }

        }

        return null;

    }

    public void Attach(World world, Entity part) {

        foreach (var module in Modules) {

            module.Attach(world, part);

        }

    }

    public override string ToString() => Id;

}
