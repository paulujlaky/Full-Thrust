namespace FullThrust.Core.Ecs;

// Queries are allocation-free ref structs; do not add or remove components of the queried types while iterating.

public readonly ref struct Row<T1> where T1 : struct {

    public readonly Entity Entity;

    public readonly ref T1 C1;

    public Row(Entity entity, ref T1 c1) {

        Entity = entity;
        C1 = ref c1;

    }

}

public readonly ref struct Row<T1, T2> where T1 : struct where T2 : struct {

    public readonly Entity Entity;

    public readonly ref T1 C1;
    public readonly ref T2 C2;

    public Row(Entity entity, ref T1 c1, ref T2 c2) {

        Entity = entity;
        C1 = ref c1;
        C2 = ref c2;

    }

}

public readonly ref struct Row<T1, T2, T3> where T1 : struct where T2 : struct where T3 : struct {

    public readonly Entity Entity;

    public readonly ref T1 C1;
    public readonly ref T2 C2;
    public readonly ref T3 C3;

    public Row(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3) {

        Entity = entity;
        C1 = ref c1;
        C2 = ref c2;
        C3 = ref c3;

    }

}

public readonly ref struct Query<T1> where T1 : struct {

    private readonly ComponentStore<T1> _s1;

    public Query(ComponentStore<T1> s1) {

        _s1 = s1;

    }

    public int Count => _s1.Count;

    public Enumerator GetEnumerator() => new(_s1);

    public ref struct Enumerator {

        private readonly ComponentStore<T1> _s1;

        private int _slot;

        public Enumerator(ComponentStore<T1> s1) {

            _s1 = s1;
            _slot = -1;

        }

        public bool MoveNext() => ++_slot < _s1.Count;

        public Row<T1> Current => new(_s1.Entities[_slot], ref _s1.GetBySlot(_slot));

    }

}

public readonly ref struct Query<T1, T2> where T1 : struct where T2 : struct {

    private readonly ComponentStore<T1> _s1;
    private readonly ComponentStore<T2> _s2;

    public Query(ComponentStore<T1> s1, ComponentStore<T2> s2) {

        _s1 = s1;
        _s2 = s2;

    }

    public Enumerator GetEnumerator() => new(_s1, _s2);

    public ref struct Enumerator {

        private readonly ComponentStore<T1> _s1;
        private readonly ComponentStore<T2> _s2;

        private int _slot;

        private int _slot2;

        public Enumerator(ComponentStore<T1> s1, ComponentStore<T2> s2) {

            _s1 = s1;
            _s2 = s2;
            _slot = -1;
            _slot2 = -1;

        }

        public bool MoveNext() {

            while (++_slot < _s1.Count) {

                _slot2 = _s2.SlotOf(_s1.Entities[_slot]);

                if (_slot2 >= 0) {

                    return true;

                }

            }

            return false;

        }

        public Row<T1, T2> Current => new(_s1.Entities[_slot], ref _s1.GetBySlot(_slot), ref _s2.GetBySlot(_slot2));

    }

}

public readonly ref struct Query<T1, T2, T3> where T1 : struct where T2 : struct where T3 : struct {

    private readonly ComponentStore<T1> _s1;
    private readonly ComponentStore<T2> _s2;
    private readonly ComponentStore<T3> _s3;

    public Query(ComponentStore<T1> s1, ComponentStore<T2> s2, ComponentStore<T3> s3) {

        _s1 = s1;
        _s2 = s2;
        _s3 = s3;

    }

    public Enumerator GetEnumerator() => new(_s1, _s2, _s3);

    public ref struct Enumerator {

        private readonly ComponentStore<T1> _s1;
        private readonly ComponentStore<T2> _s2;
        private readonly ComponentStore<T3> _s3;

        private int _slot;

        private int _slot2;
        private int _slot3;

        public Enumerator(ComponentStore<T1> s1, ComponentStore<T2> s2, ComponentStore<T3> s3) {

            _s1 = s1;
            _s2 = s2;
            _s3 = s3;
            _slot = -1;
            _slot2 = -1;
            _slot3 = -1;

        }

        public bool MoveNext() {

            while (++_slot < _s1.Count) {

                var entity = _s1.Entities[_slot];

                _slot2 = _s2.SlotOf(entity);

                if (_slot2 < 0) {

                    continue;

                }

                _slot3 = _s3.SlotOf(entity);

                if (_slot3 >= 0) {

                    return true;

                }

            }

            return false;

        }

        public Row<T1, T2, T3> Current {

            get => new(_s1.Entities[_slot], ref _s1.GetBySlot(_slot), ref _s2.GetBySlot(_slot2), ref _s3.GetBySlot(_slot3));

        }

    }

}
