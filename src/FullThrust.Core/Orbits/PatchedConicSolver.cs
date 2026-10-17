using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Simulation;

namespace FullThrust.Core.Orbits;

public readonly struct SoiTransition {

    public readonly bool Found;

    public readonly double Time;

    public readonly int FromBody;
    public readonly int ToBody;

    public SoiTransition(double time, int fromBody, int toBody) {

        Found = true;
        Time = time;
        FromBody = fromBody;
        ToBody = toBody;

    }

}

public sealed class PatchedConicSolver {

    private readonly CelestialSystem _system;

    public PatchedConicSolver(CelestialSystem system) {

        _system = system;

    }

    public double MinStep { get; set; } = 1.0;

    public double TimeTolerance { get; set; } = 1e-3;

    public int MaxSamples { get; set; } = 4096;

    public int SamplesLastSearch { get; private set; }

    // Searches (from, to] for the first sphere-of-influence crossing of a conic around `bodyIndex`.
    public SoiTransition FindNext(int bodyIndex, in StateVector epoch, double epochTime, double from, double to) {

        SamplesLastSearch = 0;

        if (to <= from) {

            return default;

        }

        var body = _system[bodyIndex];
        var elements = OrbitalElements.FromState(epoch, body.GravitationalParameter);

        var canExit = body.Parent is not null && !(elements.IsClosed && elements.Apoapsis < body.SphereOfInfluence);
        var candidates = CandidateMask(body, elements);

        if (!canExit && candidates == 0) {

            return default;

        }

        var t = from;
        var previous = from;

        while (SamplesLastSearch < MaxSamples) {

            SamplesLastSearch++;

            var state = _system.Propagator.Propagate(epoch, body.GravitationalParameter, t - epochTime);
            var hit = Evaluate(body, state, t, canExit, candidates, out var target, out var margin);

            if (hit) {

                var crossing = t == from ? t : Bisect(body, epoch, epochTime, previous, t, target);

                return new SoiTransition(crossing, bodyIndex, target);

            }

            if (t >= to) {

                return default;

            }

            previous = t;
            t = Math.Min(to, t + Math.Max(MinStep, 0.5 * margin));

        }

        return default;

    }

    private ulong CandidateMask(CelestialBody body, in OrbitalElements craft) {

        var mask = 0ul;
        var children = body.ChildList;

        for (var i = 0; i < children.Count && i < 64; i++) {

            var child = children[i];

            var low = child.Orbit.Periapsis - child.SphereOfInfluence;
            var high = child.Orbit.Apoapsis + child.SphereOfInfluence;

            if (craft.Periapsis <= high && craft.Apoapsis >= low) {

                mask |= 1ul << i;

            }

        }

        return mask;

    }

    // Returns true when the state is past a boundary; otherwise `margin` is a conservative time to the nearest one.
    private bool Evaluate(CelestialBody body, in StateVector state, double time, bool canExit, ulong candidates, out int target, out double margin) {

        target = -1;
        margin = double.PositiveInfinity;

        if (canExit) {

            var r = state.Position.Length();

            if (r >= body.SphereOfInfluence) {

                target = body.Parent!.Index;

                return true;

            }

            margin = (body.SphereOfInfluence - r) / Math.Max(state.Velocity.Length(), 1e-3);

        }

        var children = body.ChildList;

        for (var i = 0; i < children.Count && i < 64; i++) {

            if ((candidates & (1ul << i)) == 0) {

                continue;

            }

            var child = children[i];
            var childState = _system.RelativeState(child, time);

            var distance = double3.Distance(state.Position, childState.Position);

            if (distance < child.SphereOfInfluence) {

                target = child.Index;

                return true;

            }

            var closing = Math.Max((state.Velocity - childState.Velocity).Length(), 1e-3);

            margin = Math.Min(margin, (distance - child.SphereOfInfluence) / closing);

        }

        return false;

    }

    private double Bisect(CelestialBody body, in StateVector epoch, double epochTime, double outside, double inside, int target) {

        var targetIsParent = body.Parent is not null && body.Parent.Index == target;
        var targetBody = _system[target];

        while (inside - outside > TimeTolerance) {

            var mid = 0.5 * (outside + inside);
            var state = _system.Propagator.Propagate(epoch, body.GravitationalParameter, mid - epochTime);

            bool crossed;

            if (targetIsParent) {

                crossed = state.Position.Length() >= body.SphereOfInfluence;

            } else {

                var childState = _system.RelativeState(targetBody, mid);

                crossed = double3.Distance(state.Position, childState.Position) < targetBody.SphereOfInfluence;

            }

            if (crossed) {

                inside = mid;

            } else {

                outside = mid;

            }

        }

        // Return the side already past the boundary so the new patch starts inside its sphere.
        return inside;

    }

}
