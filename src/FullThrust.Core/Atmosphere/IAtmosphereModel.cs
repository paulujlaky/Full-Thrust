namespace FullThrust.Core.Atmosphere;

public readonly struct AtmosphereSample {

    public static readonly AtmosphereSample Vacuum = default;

    public readonly double Pressure;
    public readonly double Density;
    public readonly double Temperature;

    public readonly double SpeedOfSound;

    public AtmosphereSample(double pressure, double density, double temperature, double speedOfSound) {

        Pressure = pressure;
        Density = density;
        Temperature = temperature;
        SpeedOfSound = speedOfSound;

    }

    public bool IsVacuum => Pressure <= 0;

}

public interface IAtmosphereModel {

    double Height { get; }

    double SurfacePressure { get; }

    AtmosphereSample Sample(double altitude);

}
