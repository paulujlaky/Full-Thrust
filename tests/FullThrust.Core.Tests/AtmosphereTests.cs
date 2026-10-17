namespace FullThrust.Core.Tests;

public class AtmosphereTests {

    [Fact]
    public void SeaLevelMatchesStandardAtmosphere() {

        var sample = TestData.EarthMoon().Root.SampleAtmosphere(0);

        Assert.Equal(101325, sample.Pressure, 1e-6);
        Assert.Equal(288.15, sample.Temperature, 1e-9);
        Assert.Equal(1.225, sample.Density, 0.002);
        Assert.Equal(340.3, sample.SpeedOfSound, 0.5);

    }

    [Theory]
    [InlineData(5_000, 54_048, 255.7)]
    [InlineData(11_000, 22_700, 216.8)]
    [InlineData(30_000, 1_197, 226.5)]
    public void PressureAndTemperatureFollowTheLayers(double altitude, double pressure, double temperature) {

        var sample = TestData.EarthMoon().Root.SampleAtmosphere(altitude);

        Assert.InRange(sample.Pressure, pressure * 0.97, pressure * 1.03);
        Assert.InRange(sample.Temperature, temperature - 1.0, temperature + 1.0);

    }

    [Fact]
    public void PressureAndDensityFallMonotonicallyToVacuum() {

        var earth = TestData.EarthMoon().Root;
        var height = earth.AtmosphereModel!.Height;

        var previous = earth.SampleAtmosphere(0);

        for (var altitude = 500.0; altitude < height; altitude += 500) {

            var sample = earth.SampleAtmosphere(altitude);

            Assert.True(sample.Pressure < previous.Pressure);
            Assert.True(sample.Density < previous.Density);

            previous = sample;

        }

        Assert.True(earth.SampleAtmosphere(height).IsVacuum);
        Assert.True(earth.InAtmosphere(height - 1));
        Assert.False(earth.InAtmosphere(height));

    }

    [Fact]
    public void AirlessBodiesSampleVacuum() {

        var moon = TestData.EarthMoon().Find("Moon")!;

        Assert.False(moon.HasAtmosphere);
        Assert.True(moon.SampleAtmosphere(0).IsVacuum);

    }

}
