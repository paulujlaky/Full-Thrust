namespace FullThrust.Core.Ecs;

internal static class ComponentTypeCounter {

    private static int _next = -1;

    public static int Next() => Interlocked.Increment(ref _next);

}

internal static class ComponentTypeId<T> where T : struct {

    public static readonly int Value = ComponentTypeCounter.Next();

}

public sealed class World {

    private int[] _generations = new int[64];

    private int[] _freeIndices = new int[64];
    private int _freeCount;

    private int _nextIndex;
    private int _aliveCount;

    private IComponentStore?[] _stores = new IComponentStore?[16];

    public int AliveCount => _aliveCount;

    public int Capacity => _generations.Length;

    public Entity Create() {

        int index;

        if (_freeCount > 0) {

            index = _freeIndices[--_freeCount];

        } else {

            index = _nextIndex++;

            if (index >= _generations.Length) {

                Array.Resize(ref _generations, _generations.Length * 2);

            }

            _generations[index] = 1;

        }

        _aliveCount++;

        return new Entity(index, _generations[index]);

    }

    public bool IsAlive(Entity entity) {

        return !entity.IsNull
            && (uint)entity.Index < (uint)_nextIndex
            && _generations[entity.Index] == entity.Generation;

    }

    public bool Destroy(Entity entity) {

        if (!IsAlive(entity)) {

            return false;

        }

        for (var i = 0; i < _stores.Length; i++) {

            _stores[i]?.Remove(entity);

        }

        // Skip generation 0 on wrap so a recycled slot never looks like Entity.Null.
        var next = _generations[entity.Index] + 1;

        _generations[entity.Index] = next == 0 ? 1 : next;

        if (_freeCount == _freeIndices.Length) {

            Array.Resize(ref _freeIndices, _freeIndices.Length * 2);

        }

        _freeIndices[_freeCount++] = entity.Index;
        _aliveCount--;

        return true;

    }

    public ComponentStore<T> Store<T>() where T : struct {

        var id = ComponentTypeId<T>.Value;

        if (id >= _stores.Length) {

            Array.Resize(ref _stores, Math.Max(id + 1, _stores.Length * 2));

        }

        if (_stores[id] is ComponentStore<T> store) {

            return store;

        }

        store = new ComponentStore<T>();
        _stores[id] = store;

        return store;

    }

    public ref T Add<T>(Entity entity, in T component) where T : struct {

        ThrowIfDead(entity);

        return ref Store<T>().Add(entity, component);

    }

    public bool Remove<T>(Entity entity) where T : struct => Store<T>().Remove(entity);

    public bool Has<T>(Entity entity) where T : struct => Store<T>().Has(entity);

    public ref T Get<T>(Entity entity) where T : struct => ref Store<T>().Get(entity);

    public Query<T1> Query<T1>() where T1 : struct => new(Store<T1>());

    public Query<T1, T2> Query<T1, T2>() where T1 : struct where T2 : struct => new(Store<T1>(), Store<T2>());

    public Query<T1, T2, T3> Query<T1, T2, T3>() where T1 : struct where T2 : struct where T3 : struct {

        return new(Store<T1>(), Store<T2>(), Store<T3>());

    }

    private void ThrowIfDead(Entity entity) {

        if (!IsAlive(entity)) {

            throw new InvalidOperationException($"{entity} is not alive.");

        }

    }

}
