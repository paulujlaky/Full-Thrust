using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Orbits;

namespace FullThrust.Core.Tests;

public class CelestialSystemTests {

    [Fact]
    public void LoadsEarthMoonDataWithEarthAsRoot() {

        var system = TestData.EarthMoon();

        Assert.Equal(2, system.Count);
        Assert.Equal("Earth", system.Root.Name);
        Assert.True(system.Root.HasAtmosphere);
        Assert.Equal(8, system.Root.Atmosphere!.Layers.Count);

        var moon = system.Find("moon")!;

        Assert.Same(system.Root, moon.Parent);
        Assert.Single(system.Root.Children);
        Assert.False(moon.HasAtmosphere);

    }

    [Fact]
    public void DerivesMoonSphereOfInfluenceAndPeriod() {

        var moon = TestData.EarthMoon().Find("Moon")!;

        Assert.Equal(66_100_000, moon.SphereOfInfluence, 300_000.0);
        Assert.Equal(27.32, moon.OrbitalPeriod / 86400, 0.05);

    }

    [Fact]
    public void MoonStaysWithinItsApsidesOverAnOrbit() {

        var system = TestData.EarthMoon();
        var moon = system.Find("Moon")!;

        for (var t = 0.0; t < moon.OrbitalPeriod; t += moon.OrbitalPeriod / 50) {

            system.Update(t);

            var distance = system.Position(moon.Index).Length();

            Assert.InRange(distance, moon.Orbit.Periapsis - 1, moon.Orbit.Apoapsis + 1);

        }

    }

    [Fact]
    public void MoonOrbitIsInclinedToReferencePlane() {

        var system = TestData.EarthMoon();
        var moon = system.Find("Moon")!;

        var state = system.RelativeState(moon, 12345);
        var inclination = OrbitalElements.FromState(state, system.Root.GravitationalParameter).Inclination;

        Assert.Equal(5.145, inclination * 180 / Math.PI, 1e-6);

    }

    [Fact]
    public void RebasePreservesAbsoluteState() {

        var system = TestData.EarthMoon();
        var moon = system.Find("Moon")!;
        var ut = 50_000.0;

        var relativeToEarth = new Simulation.StateVector(new double3(3e8, 1e7, -2e7), new double3(100, 900, 5));
        var relativeToMoon = system.Rebase(relativeToEarth, 0, moon.Index, ut);
        var back = system.Rebase(relativeToMoon, moon.Index, 0, ut);

        Assert.True(double3.Distance(relativeToEarth.Position, back.Position) < 1e-6);
        Assert.True(double3.Distance(relativeToEarth.Velocity, back.Velocity) < 1e-9);

    }

    [Fact]
    public void DominantBodyIsMoonInsideItsSphere() {

        var system = TestData.EarthMoon();
        var moon = system.Find("Moon")!;
        var ut = 1000.0;

        var moonPosition = system.StateAt(moon.Index, ut).Position;

        Assert.Same(moon, system.DominantBodyAt(moonPosition + new double3(1e7, 0, 0), ut));
        Assert.Same(system.Root, system.DominantBodyAt(moonPosition + new double3(1e8, 0, 0), ut));

    }

    [Fact]
    public void PlanetRotatesOncePerSiderealDay() {

        var earth = TestData.EarthMoon().Root;

        var start = earth.RotationAt(0) * double3.UnitX;
        var half = earth.RotationAt(earth.RotationPeriod / 2) * double3.UnitX;
        var full = earth.RotationAt(earth.RotationPeriod) * double3.UnitX;

        Assert.True(double3.Distance(start, full) < 1e-9);
        Assert.Equal(-1.0, double3.Dot(start, half), 1e-9);

    }

    [Theory]
    [InlineData("""{ "bodies": [] }""")]
    [InlineData("""{ "bodies": [ { "name": "A", "mass": 1, "radius": 1 }, { "name": "B", "mass": 1, "radius": 1 } ] }""")]
    [InlineData("""{ "bodies": [ { "name": "A", "mass": 1, "radius": 1 }, { "name": "B", "parent": "A", "mass": 1, "radius": 1 } ] }""")]
    [InlineData("""{ "bodies": [ { "name": "A", "mass": 1, "radius": 1 }, { "name": "B", "parent": "X", "mass": 1, "radius": 1, "orbit": { "semiMajorAxis": 10 } } ] }""")]
    [InlineData("""{ "bodies": [ { "name": "A", "mass": 1, "radius": 0 } ] }""")]
    public void RejectsInvalidDefinitions(string json) {

        Assert.Throws<InvalidDataException>(() => CelestialSystemLoader.Load(json, new KeplerPropagator()));

    }

}
