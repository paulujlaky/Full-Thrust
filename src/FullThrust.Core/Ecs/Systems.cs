using FullThrust.Core.Simulation;

namespace FullThrust.Core.Ecs;

public enum SystemPhase {

    FixedUpdate,
    Update,
    LateUpdate,

}

public interface ISystem {

    void Update(World world, in SimTime time);

}

public sealed class SystemScheduler {

    private readonly List<ISystem>[] _phases;

    public SystemScheduler() {

        var count = Enum.GetValues<SystemPhase>().Length;

        _phases = new List<ISystem>[count];

        for (var i = 0; i < count; i++) {

            _phases[i] = [];

        }

    }

    public SystemScheduler Add(SystemPhase phase, ISystem system) {

        _phases[(int)phase].Add(system);

        return this;

    }

    public bool Remove(SystemPhase phase, ISystem system) => _phases[(int)phase].Remove(system);

    public int CountIn(SystemPhase phase) => _phases[(int)phase].Count;

    public void Run(SystemPhase phase, World world, in SimTime time) {

        var systems = _phases[(int)phase];

        for (var i = 0; i < systems.Count; i++) {

            systems[i].Update(world, time);

        }

    }

}
