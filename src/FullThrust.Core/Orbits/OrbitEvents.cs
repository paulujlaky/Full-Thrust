using FullThrust.Core.Ecs;

namespace FullThrust.Core.Orbits;

public readonly struct SoiTransitionEvent {

    public readonly Entity Entity;

    public readonly int FromBody;
    public readonly int ToBody;

    public readonly double Time;

    public SoiTransitionEvent(Entity entity, int fromBody, int toBody, double time) {

        Entity = entity;
        FromBody = fromBody;
        ToBody = toBody;
        Time = time;

    }

}

public sealed class OrbitEvents {

    private readonly SoiTransitionEvent[] _recent;

    private int _head;

    public OrbitEvents(int capacity = 16) {

        _recent = new SoiTransitionEvent[capacity];

    }

    public long TransitionCount { get; private set; }

    public int RecentCount => (int)Math.Min(TransitionCount, _recent.Length);

    // Index 0 is the most recent event.
    public SoiTransitionEvent Recent(int index) => _recent[(_head - 1 - index + _recent.Length * 2) % _recent.Length];

    public void Record(in SoiTransitionEvent transition) {

        _recent[_head] = transition;
        _head = (_head + 1) % _recent.Length;

        TransitionCount++;

    }

}
