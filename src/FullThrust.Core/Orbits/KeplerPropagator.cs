using Brutal.Numerics;
using FullThrust.Core.Simulation;

namespace FullThrust.Core.Orbits;

// Universal-variable formulation, so elliptic, parabolic and hyperbolic orbits share one path.
public sealed class KeplerPropagator : IOrbitPropagator {

    private const int MaxIterations = 64;

    private const double Tolerance = 1e-12;

    public StateVector Propagate(in StateVector state, double gravitationalParameter, double deltaTime) {

        if (deltaTime == 0) {

            return state;

        }

        var mu = gravitationalParameter;
        var sqrtMu = Math.Sqrt(mu);

        var r0Vec = state.Position;
        var v0Vec = state.Velocity;

        var r0 = r0Vec.Length();
        var v0 = v0Vec.Length();

        var rDotV = double3.Dot(r0Vec, v0Vec);
        var alpha = 2.0 / r0 - v0 * v0 / mu;

        var dt = deltaTime;

        if (alpha > 1e-12) {

            // Wrap whole periods away so Newton starts close for multi-orbit steps.
            var period = 2.0 * Math.PI * Math.Sqrt(1.0 / (alpha * alpha * alpha * mu));

            dt = Math.IEEERemainder(dt, period);

        }

        var chi = InitialGuess(alpha, r0, rDotV, mu, sqrtMu, dt);

        double z = 0, c = 0.5, s = 1.0 / 6.0;

        for (var i = 0; i < MaxIterations; i++) {

            z = alpha * chi * chi;
            Stumpff(z, out c, out s);

            var chi2 = chi * chi;
            var chi3 = chi2 * chi;

            var f = rDotV / sqrtMu * chi2 * c + (1 - alpha * r0) * chi3 * s + r0 * chi - sqrtMu * dt;
            var df = rDotV / sqrtMu * chi * (1 - z * s) + (1 - alpha * r0) * chi2 * c + r0;

            var step = f / df;

            chi -= step;

            if (Math.Abs(step) <= Tolerance * Math.Max(1.0, Math.Abs(chi))) {

                break;

            }

        }

        z = alpha * chi * chi;
        Stumpff(z, out c, out s);

        var chiSq = chi * chi;

        var lagrangeF = 1 - chiSq / r0 * c;
        var lagrangeG = dt - chiSq * chi / sqrtMu * s;

        var position = r0Vec * lagrangeF + v0Vec * lagrangeG;
        var r = position.Length();

        var lagrangeFDot = sqrtMu / (r * r0) * (z * chi * s - chi);
        var lagrangeGDot = 1 - chiSq / r * c;

        var velocity = r0Vec * lagrangeFDot + v0Vec * lagrangeGDot;

        return new StateVector(position, velocity);

    }

    private static double InitialGuess(double alpha, double r0, double rDotV, double mu, double sqrtMu, double dt) {

        if (alpha > 1e-12) {

            return sqrtMu * dt * alpha;

        }

        if (alpha < -1e-12) {

            var a = 1.0 / alpha;
            var sign = Math.Sign(dt);
            var denominator = rDotV + sign * Math.Sqrt(-mu * a) * (1 - r0 * alpha);
            var argument = -2.0 * mu * alpha * dt / denominator;

            if (argument > 0) {

                return sign * Math.Sqrt(-a) * Math.Log(argument);

            }

        }

        return sqrtMu * dt / r0;

    }

    public static void Stumpff(double z, out double c, out double s) {

        if (z > 1e-6) {

            var sz = Math.Sqrt(z);

            c = (1 - Math.Cos(sz)) / z;
            s = (sz - Math.Sin(sz)) / (sz * sz * sz);

        } else if (z < -1e-6) {

            var sz = Math.Sqrt(-z);

            c = (Math.Cosh(sz) - 1) / -z;
            s = (Math.Sinh(sz) - sz) / (sz * sz * sz);

        } else {

            c = 0.5 - z / 24.0 + z * z / 720.0;
            s = 1.0 / 6.0 - z / 120.0 + z * z / 5040.0;

        }

    }

}
