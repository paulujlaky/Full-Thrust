using Brutal.Numerics;
using FullThrust.Core.Orbits;
using FullThrust.Core.Simulation;

namespace FullThrust.Core.Tests;

public class KeplerTests {

    private const double EarthMu = 3.986004418e14;

    private readonly KeplerPropagator _propagator = new();

    private static double Energy(in StateVector s, double mu) => s.Velocity.LengthSquared() / 2 - mu / s.Position.Length();

    private static double3 AngularMomentum(in StateVector s) => double3.Cross(s.Position, s.Velocity);

    [Fact]
    public void CircularOrbitReturnsAfterOnePeriod() {

        var radius = 6_571_000.0;
        var start = OrbitScenarios.Circular(EarthMu, radius, 0.3);
        var period = 2 * Math.PI * Math.Sqrt(radius * radius * radius / EarthMu);

        var end = _propagator.Propagate(start, EarthMu, period);

        Assert.True(double3.Distance(start.Position, end.Position) < 1e-3);
        Assert.Equal(88.36, period / 60, 0.05);

    }

    [Fact]
    public void QuarterPeriodOfCircularOrbitMovesNinetyDegrees() {

        var radius = 7_000_000.0;
        var start = OrbitScenarios.Circular(EarthMu, radius);
        var period = 2 * Math.PI * Math.Sqrt(radius * radius * radius / EarthMu);

        var end = _propagator.Propagate(start, EarthMu, period / 4);

        Assert.Equal(0.0, double3.Dot(start.Position, end.Position) / (radius * radius), 1e-9);

    }

    [Theory]
    [InlineData(0.1, 1234.5)]
    [InlineData(0.7, 20_000)]
    [InlineData(0.97, 400_000)]
    [InlineData(1.5, 50_000)]
    [InlineData(3.0, -10_000)]
    public void ConservesEnergyAndAngularMomentum(double eccentricity, double deltaTime) {

        var periapsis = 6_700_000.0;

        var elements = new OrbitalElements {

            SemiMajorAxis = periapsis / (1 - eccentricity),
            Eccentricity = eccentricity,
            Inclination = 0.5,
            LongitudeOfAscendingNode = 1.0,
            ArgumentOfPeriapsis = 2.0,
            TrueAnomaly = 0.2,

        };

        var start = elements.ToState(EarthMu);
        var end = _propagator.Propagate(start, EarthMu, deltaTime);

        var energy = Energy(start, EarthMu);
        var momentum = AngularMomentum(start);

        Assert.Equal(1.0, Energy(end, EarthMu) / energy, 1e-9);
        Assert.True(double3.Distance(momentum, AngularMomentum(end)) / momentum.Length() < 1e-9);

    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(2.0)]
    public void ForwardThenBackwardReturnsToStart(double eccentricity) {

        var periapsis = 7_000_000.0;

        var elements = new OrbitalElements {

            SemiMajorAxis = periapsis / (1 - eccentricity),
            Eccentricity = eccentricity,
            Inclination = 0.1,
            TrueAnomaly = -0.4,

        };

        var start = elements.ToState(EarthMu);
        var forward = _propagator.Propagate(start, EarthMu, 3600);
        var back = _propagator.Propagate(forward, EarthMu, -3600);

        Assert.True(double3.Distance(start.Position, back.Position) < 1e-3);
        Assert.True(double3.Distance(start.Velocity, back.Velocity) < 1e-6);

    }

    [Fact]
    public void ManyPeriodsStayAccurate() {

        var radius = 384_399_000.0;
        var start = OrbitScenarios.Circular(EarthMu, radius, 0.09);
        var period = 2 * Math.PI * Math.Sqrt(radius * radius * radius / EarthMu);

        var end = _propagator.Propagate(start, EarthMu, period * 1000 + period / 2);

        Assert.True(double3.Distance(-start.Position, end.Position) / radius < 1e-8);

    }

}
