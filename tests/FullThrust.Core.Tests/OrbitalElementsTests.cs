using Brutal.Numerics;
using FullThrust.Core.Orbits;

namespace FullThrust.Core.Tests;

public class OrbitalElementsTests {

    private const double Mu = 3.986004418e14;

    [Theory]
    [InlineData(7_000_000, 0.1, 0.5, 1.0, 2.0, 0.3)]
    [InlineData(24_000_000, 0.7, 1.2, 4.0, 0.5, 3.5)]
    [InlineData(-20_000_000, 1.4, 0.2, 0.3, 5.0, 0.6)]
    [InlineData(10_000_000, 0.3, 3.0, 2.5, 1.5, 5.9)]
    public void RoundTripsThroughStateVector(double a, double e, double i, double lan, double argPe, double nu) {

        var elements = new OrbitalElements {

            SemiMajorAxis = a,
            Eccentricity = e,
            Inclination = i,
            LongitudeOfAscendingNode = lan,
            ArgumentOfPeriapsis = argPe,
            TrueAnomaly = nu,

        };

        var back = OrbitalElements.FromState(elements.ToState(Mu), Mu);

        Assert.Equal(a, back.SemiMajorAxis, Math.Abs(a) * 1e-9);
        Assert.Equal(e, back.Eccentricity, 1e-9);
        Assert.Equal(i, back.Inclination, 1e-9);
        Assert.Equal(lan, back.LongitudeOfAscendingNode, 1e-9);
        Assert.Equal(argPe, back.ArgumentOfPeriapsis, 1e-9);
        Assert.Equal(nu, back.TrueAnomaly, 1e-9);

    }

    [Fact]
    public void EquatorialProgradeOrbitHasZeroInclinationAndNorthPointingMomentum() {

        var state = OrbitScenarios.Circular(Mu, 7_000_000);
        var momentum = double3.Cross(state.Position, state.Velocity);

        var elements = OrbitalElements.FromState(state, Mu);

        Assert.Equal(0.0, elements.Inclination, 1e-12);
        Assert.True(momentum.Y > 0);
        Assert.Equal(7_000_000, elements.Periapsis, 1e-3);
        Assert.Equal(7_000_000, elements.Apoapsis, 1e-3);

    }

    [Fact]
    public void CircularOrbitPhaseIsMeasuredFromReference() {

        var state = OrbitScenarios.Circular(Mu, 7_000_000, 0, 1.0);

        Assert.Equal(1.0, OrbitalElements.FromState(state, Mu).TrueAnomaly, 1e-9);

    }

    [Fact]
    public void MeanAnomalyConvertsToTrueAnomaly() {

        Assert.Equal(0.0, OrbitalElements.TrueFromMeanAnomaly(0, 0.5), 1e-12);
        Assert.Equal(Math.PI, Math.Abs(OrbitalElements.TrueFromMeanAnomaly(Math.PI, 0.5)), 1e-12);
        Assert.Equal(1.0, OrbitalElements.TrueFromMeanAnomaly(1.0, 0), 1e-12);

        var nu = OrbitalElements.TrueFromMeanAnomaly(1.0, 0.3);

        Assert.True(nu > 1.0);

    }

    [Fact]
    public void ClosedOrbitPeriodMatchesKeplersThirdLaw() {

        var elements = new OrbitalElements { SemiMajorAxis = 42_164_000, Eccentricity = 0 };

        Assert.Equal(86_164, elements.Period(Mu), 2.0);
        Assert.Equal(double.PositiveInfinity, new OrbitalElements { SemiMajorAxis = -1e7, Eccentricity = 1.5 }.Period(Mu));

    }

}
