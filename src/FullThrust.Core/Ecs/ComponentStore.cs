using System.Runtime.CompilerServices;

namespace FullThrust.Core.Ecs;

public interface IComponentStore {

    int Count { get; }

    Type ComponentType { get; }

    bool Has(Entity entity);

    bool Remove(Entity entity);

}

public sealed class ComponentStore<T> : IComponentStore where T : struct {

    private const int InitialCapacity = 16;

    // Maps entity index to dense slot + 1, so zero means "absent".
    private int[] _sparse = new int[InitialCapacity];

    private Entity[] _entities = new Entity[InitialCapacity];
    private T[] _components = new T[InitialCapacity];

    private int _count;

    public int Count => _count;

    public Type ComponentType => typeof(T);

    public ReadOnlySpan<Entity> Entities => new(_entities, 0, _count);

    public Span<T> Components => new(_components, 0, _count);

    public bool Has(Entity entity) {

        if ((uint)entity.Index >= (uint)_sparse.Length) {

            return false;

        }

        var slot = _sparse[entity.Index] - 1;

        return slot >= 0 && _entities[slot] == entity;

    }

    public ref T Add(Entity entity, in T component) {

        if (Has(entity)) {

            throw new InvalidOperationException($"{entity} already has component {typeof(T).Name}.");

        }

        EnsureSparse(entity.Index);
        EnsureDense(_count + 1);

        var slot = _count++;

        _entities[slot] = entity;
        _components[slot] = component;
        _sparse[entity.Index] = slot + 1;

        return ref _components[slot];

    }

    public bool Remove(Entity entity) {

        if (!Has(entity)) {

            return false;

        }

        var slot = _sparse[entity.Index] - 1;
        var last = --_count;

        if (slot != last) {

            var moved = _entities[last];

            _entities[slot] = moved;
            _components[slot] = _components[last];
            _sparse[moved.Index] = slot + 1;

        }

        _entities[last] = Entity.Null;
        _components[last] = default;
        _sparse[entity.Index] = 0;

        return true;

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get(Entity entity) {

        if (!Has(entity)) {

            throw new KeyNotFoundException($"{entity} has no component {typeof(T).Name}.");

        }

        return ref _components[_sparse[entity.Index] - 1];

    }

    public ref T TryGet(Entity entity, out bool found) {

        found = Has(entity);

        if (!found) {

            return ref Unsafe.NullRef<T>();

        }

        return ref _components[_sparse[entity.Index] - 1];

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref T GetBySlot(int slot) => ref _components[slot];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int SlotOf(Entity entity) {

        if ((uint)entity.Index >= (uint)_sparse.Length) {

            return -1;

        }

        var slot = _sparse[entity.Index] - 1;

        return slot >= 0 && _entities[slot] == entity ? slot : -1;

    }

    private void EnsureSparse(int index) {

        if (index < _sparse.Length) {

            return;

        }

        Array.Resize(ref _sparse, Math.Max(index + 1, _sparse.Length * 2));

    }

    private void EnsureDense(int required) {

        if (required <= _components.Length) {

            return;

        }

        var capacity = Math.Max(required, _components.Length * 2);

        Array.Resize(ref _entities, capacity);
        Array.Resize(ref _components, capacity);

    }

}
