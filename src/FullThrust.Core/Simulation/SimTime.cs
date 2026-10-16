namespace FullThrust.Core.Simulation;

public readonly struct SimTime {

    public readonly double DeltaTime;

    public readonly double UniversalTime;

    public readonly double RealDeltaTime;

    public readonly long Tick;

    public readonly bool OnRails;

    public SimTime(double deltaTime, double universalTime, double realDeltaTime, long tick, bool onRails) {

        DeltaTime = deltaTime;
        UniversalTime = universalTime;
        RealDeltaTime = realDeltaTime;
        Tick = tick;
        OnRails = onRails;

    }

}
