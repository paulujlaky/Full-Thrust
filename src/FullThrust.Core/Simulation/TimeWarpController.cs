namespace FullThrust.Core.Simulation;

public interface IWarpConstraint {

    // `reason` must be a static string so the per-frame check never allocates.
    bool Allows(TimeWarpMode mode, double rate, out string reason);

}

public sealed class TimeWarpController : ITimeWarp {

    public static readonly double[] DefaultRailsRates = [1, 5, 10, 50, 100, 1_000, 10_000, 100_000, 1_000_000];

    // Physics warp slots in here later; index 0 must stay 1x.
    public static readonly double[] DefaultPhysicsRates = [1];

    private readonly double[] _railsRates;
    private readonly double[] _physicsRates;

    private readonly List<IWarpConstraint> _constraints = [];

    private int _railsIndex;
    private int _physicsIndex;

    public TimeWarpController(double[]? railsRates = null, double[]? physicsRates = null) {

        _railsRates = railsRates ?? DefaultRailsRates;
        _physicsRates = physicsRates ?? DefaultPhysicsRates;

        if (_railsRates[0] != 1 || _physicsRates[0] != 1) {

            throw new ArgumentException("Warp rate tables must start at 1x.");

        }

    }

    public ReadOnlySpan<double> RailsRates => _railsRates;

    public ReadOnlySpan<double> PhysicsRates => _physicsRates;

    public TimeWarpMode RequestedMode { get; private set; } = TimeWarpMode.Rails;

    public int Level => RequestedMode == TimeWarpMode.Rails ? _railsIndex : _physicsIndex;

    public double Rate => RequestedMode == TimeWarpMode.Rails ? _railsRates[_railsIndex] : _physicsRates[_physicsIndex];

    // 1x always simulates normally; rails only engage once actually warping.
    public TimeWarpMode Mode => RequestedMode == TimeWarpMode.Rails && _railsIndex > 0 ? TimeWarpMode.Rails : TimeWarpMode.Physics;

    public bool CanWarp => CanEnter(RequestedMode, NextRate(), out _);

    public string? BlockedReason { get; private set; }

    public void AddConstraint(IWarpConstraint constraint) => _constraints.Add(constraint);

    public void SelectMode(TimeWarpMode mode) {

        if (mode == RequestedMode) {

            return;

        }

        Stop();
        RequestedMode = mode;

    }

    public bool Increase() => SetLevel(Level + 1);

    public bool Decrease() => SetLevel(Level - 1);

    public void Stop() {

        _railsIndex = 0;
        _physicsIndex = 0;

    }

    public bool SetLevel(int level) {

        var rates = RequestedMode == TimeWarpMode.Rails ? _railsRates : _physicsRates;

        level = Math.Clamp(level, 0, rates.Length - 1);

        if (level > 0 && !CanEnter(RequestedMode, rates[level], out var reason)) {

            BlockedReason = reason;

            return false;

        }

        BlockedReason = null;

        if (RequestedMode == TimeWarpMode.Rails) {

            _railsIndex = level;

        } else {

            _physicsIndex = level;

        }

        return true;

    }

    // Re-checks constraints each frame and drops to 1x as soon as one fails mid-warp.
    public void Update() {

        if (Level == 0) {

            return;

        }

        if (!CanEnter(RequestedMode, Rate, out var reason)) {

            BlockedReason = reason;
            Stop();

        }

    }

    private double NextRate() {

        var rates = RequestedMode == TimeWarpMode.Rails ? _railsRates : _physicsRates;

        return rates[Math.Min(Level + 1, rates.Length - 1)];

    }

    private bool CanEnter(TimeWarpMode mode, double rate, out string reason) {

        for (var i = 0; i < _constraints.Count; i++) {

            if (!_constraints[i].Allows(mode, rate, out reason)) {

                return false;

            }

        }

        reason = "";

        return true;

    }

}
