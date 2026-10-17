using FullThrust.Core.Bodies;
using FullThrust.Core.Simulation;

namespace FullThrust.Core.Orbits;

public struct TrajectoryPatch {

    public int Body;

    public StateVector Epoch;
    public double EpochTime;

    public double StartTime;
    public double EndTime;

    public bool EndsInTransition;
    public int NextBody;

}

public sealed class TrajectoryPredictor {

    private readonly CelestialSystem _system;
    private readonly PatchedConicSolver _solver;

    private readonly TrajectoryPatch[] _patches;

    private int _sourceVersion = -1;
    private int _sourceBody = -1;

    public TrajectoryPredictor(CelestialSystem system, PatchedConicSolver solver, int maxPatches = 4) {

        _system = system;
        _solver = solver;
        _patches = new TrajectoryPatch[maxPatches];

    }

    public double OpenOrbitHorizon { get; set; } = 30 * 86400.0;

    public int Count { get; private set; }

    public ReadOnlySpan<TrajectoryPatch> Patches => new(_patches, 0, Count);

    public double TimeToNextTransition(double now) {

        return Count > 0 && _patches[0].EndsInTransition ? _patches[0].EndTime - now : double.PositiveInfinity;

    }

    public void Reset() => Count = 0;

    public void Update(in OrbitState orbit, double now) {

        var stale = orbit.Version != _sourceVersion || orbit.Body != _sourceBody || Count == 0 || now > _patches[0].EndTime;

        if (!stale) {

            return;

        }

        _sourceVersion = orbit.Version;
        _sourceBody = orbit.Body;

        Count = 0;

        var body = orbit.Body;
        var epoch = orbit.Epoch;
        var epochTime = orbit.EpochTime;
        var start = now;

        while (Count < _patches.Length) {

            var mu = _system[body].GravitationalParameter;
            var elements = OrbitalElements.FromState(epoch, mu);
            var horizon = elements.IsClosed ? elements.Period(mu) : OpenOrbitHorizon;

            var transition = _solver.FindNext(body, epoch, epochTime, start, start + horizon);

            ref var patch = ref _patches[Count++];

            patch.Body = body;
            patch.Epoch = epoch;
            patch.EpochTime = epochTime;
            patch.StartTime = start;
            patch.EndTime = transition.Found ? transition.Time : start + horizon;
            patch.EndsInTransition = transition.Found;
            patch.NextBody = transition.Found ? transition.ToBody : body;

            if (!transition.Found) {

                break;

            }

            var atCrossing = _system.Propagator.Propagate(epoch, mu, transition.Time - epochTime);

            epoch = _system.Rebase(atCrossing, body, transition.ToBody, transition.Time);
            epochTime = transition.Time;
            start = transition.Time;
            body = transition.ToBody;

        }

    }

}
