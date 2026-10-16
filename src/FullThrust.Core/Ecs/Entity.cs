namespace FullThrust.Core.Ecs;

public readonly struct Entity : IEquatable<Entity> {

    public static readonly Entity Null = default;

    public readonly int Index;

    // Generation 0 is never issued, so default(Entity) is always invalid.
    public readonly int Generation;

    public Entity(int index, int generation) {

        Index = index;
        Generation = generation;

    }

    public bool IsNull => Generation == 0;

    public bool Equals(Entity other) => Index == other.Index && Generation == other.Generation;

    public override bool Equals(object? obj) => obj is Entity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Index, Generation);

    public static bool operator ==(Entity left, Entity right) => left.Equals(right);

    public static bool operator !=(Entity left, Entity right) => !left.Equals(right);

    public override string ToString() => $"Entity({Index}:{Generation})";

}
