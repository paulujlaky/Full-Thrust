using System.Runtime.CompilerServices;

namespace FullThrust.Core.Parts;

public static class PhysicalConstants {

    // Isp is defined against standard gravity regardless of where the engine fires.
    public const double StandardGravity = 9.80665;

    public const double SeaLevelPressure = 101325.0;

    public const double HeliumMolarMass = 0.0040026;

    public const double PressurantTemperature = 290.0;

}

[InlineArray(Capacity)]
public struct ResourceSet {

    public const int Capacity = 8;

    private double _element;

    public readonly double Sum() {

        var total = 0.0;

        for (var i = 0; i < Capacity; i++) {

            total += this[i];

        }

        return total;

    }

}

public sealed class ResourceDefinition {

    public required int Id { get; init; }

    public required string Name { get; init; }

    public required double Density { get; init; }

    public override string ToString() => Name;

}
