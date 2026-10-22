namespace FullThrust.Core.Simulation;

public sealed class SimulationClock {

    private double _accumulator;

    private int _pendingSteps;

    private double _frameRealDelta;
    private double _frameSimDelta;

    public SimulationClock(ITimeWarp warp, double universalTime = 0) {

        Warp = warp;
        UniversalTime = universalTime;

    }

    public ITimeWarp Warp { get; set; }

    public double FixedDeltaTime { get; set; } = 1.0 / 50.0;

    public int MaxStepsPerFrame { get; set; } = 8;

    public double TimeScale { get; set; } = 1.0;

    public bool Paused { get; set; }

    public double UniversalTime { get; private set; }

    public long Tick { get; private set; }

    public int StepsLastFrame { get; private set; }

    public long DroppedSteps { get; private set; }

    public double InterpolationAlpha => FixedDeltaTime > 0 ? _accumulator / FixedDeltaTime : 0;

    public SimTime FrameTime => new(_frameSimDelta, UniversalTime, _frameRealDelta, Tick, Warp.Mode == TimeWarpMode.Rails);

    public int Advance(double realDeltaTime) {

        _frameRealDelta = Math.Max(0, realDeltaTime);
        _frameSimDelta = Paused ? 0 : _frameRealDelta * TimeScale * Warp.Rate;

        if (Warp.Mode == TimeWarpMode.Rails) {

            UniversalTime += _frameSimDelta;
            _accumulator = 0;
            _pendingSteps = 0;
            StepsLastFrame = 0;

            return 0;

        }

        _accumulator += _frameSimDelta;

        var steps = (int)Math.Floor(_accumulator / FixedDeltaTime);

        if (steps > MaxStepsPerFrame) {

            // Drop the backlog instead of spiralling when a frame takes too long.
            DroppedSteps += steps - MaxStepsPerFrame;
            _accumulator -= (steps - MaxStepsPerFrame) * FixedDeltaTime;
            steps = MaxStepsPerFrame;

        }

        _pendingSteps = steps;
        StepsLastFrame = steps;

        return steps;

    }

    public SimTime NextFixedStep() {

        if (_pendingSteps <= 0) {

            throw new InvalidOperationException("No fixed steps pending; call Advance first.");

        }

        _pendingSteps--;
        _accumulator -= FixedDeltaTime;

        UniversalTime += FixedDeltaTime;
        Tick++;

        return new SimTime(FixedDeltaTime, UniversalTime, _frameRealDelta, Tick, false);

    }

}
