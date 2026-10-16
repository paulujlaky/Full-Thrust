namespace FullThrust.Core.Simulation;

public enum TimeWarpMode {

    // Physics integrates with fixed steps; warp > 1 means more steps per frame.
    Physics,

    // Physics is suspended and state is propagated analytically.
    Rails,

}

public interface ITimeWarp {

    double Rate { get; }

    TimeWarpMode Mode { get; }

    bool CanWarp { get; }

}

public sealed class RealTimeWarp : ITimeWarp {

    public double Rate => 1.0;

    public TimeWarpMode Mode => TimeWarpMode.Physics;

    public bool CanWarp => false;

}
