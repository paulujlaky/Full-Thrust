using FullThrust.Core.Bodies;

namespace FullThrust.Core.Atmosphere;

// Piecewise-linear temperature layers with hydrostatic pressure, as in the US Standard Atmosphere.
public sealed class LayeredAtmosphere : IAtmosphereModel {

    public const double GasConstant = 8.31446261815324;

    private const double HeatCapacityRatio = 1.4;

    private readonly double[] _baseAltitudes;
    private readonly double[] _baseTemperatures;
    private readonly double[] _lapseRates;
    private readonly double[] _basePressures;

    private readonly double _molarMass;
    private readonly double _gravity;
    private readonly double _radius;

    public LayeredAtmosphere(AtmosphereData data, double surfaceGravity, double radius) {

        if (data.Layers.Count == 0) {

            throw new InvalidDataException("An atmosphere needs at least one temperature layer.");

        }

        var count = data.Layers.Count;

        _baseAltitudes = new double[count];
        _baseTemperatures = new double[count];
        _lapseRates = new double[count];
        _basePressures = new double[count];

        _molarMass = data.MolarMass;
        _gravity = surfaceGravity;
        _radius = radius;

        Height = data.Height;
        SurfacePressure = data.SurfacePressure;

        for (var i = 0; i < count; i++) {

            var layer = data.Layers[i];

            _baseAltitudes[i] = layer.BaseAltitude;
            _baseTemperatures[i] = layer.BaseTemperature;
            _lapseRates[i] = layer.LapseRate;

        }

        _basePressures[0] = data.SurfacePressure;

        for (var i = 1; i < count; i++) {

            _basePressures[i] = PressureInLayer(i - 1, _baseAltitudes[i], out _);

        }

    }

    public double Height { get; }

    public double SurfacePressure { get; }

    public AtmosphereSample Sample(double altitude) {

        if (altitude >= Height) {

            return AtmosphereSample.Vacuum;

        }

        // Layer bases are geopotential altitudes, which fold in gravity falling off with height.
        var geopotential = _radius * altitude / (_radius + altitude);

        var layer = 0;

        while (layer + 1 < _baseAltitudes.Length && geopotential >= _baseAltitudes[layer + 1]) {

            layer++;

        }

        var pressure = PressureInLayer(layer, geopotential, out var temperature);
        var density = pressure * _molarMass / (GasConstant * temperature);
        var speedOfSound = Math.Sqrt(HeatCapacityRatio * GasConstant * temperature / _molarMass);

        return new AtmosphereSample(pressure, density, temperature, speedOfSound);

    }

    private double PressureInLayer(int layer, double geopotential, out double temperature) {

        var baseAltitude = _baseAltitudes[layer];
        var baseTemperature = _baseTemperatures[layer];
        var lapse = _lapseRates[layer];
        var basePressure = _basePressures[layer];

        temperature = Math.Max(1.0, baseTemperature + lapse * (geopotential - baseAltitude));

        var exponent = _gravity * _molarMass / GasConstant;

        if (Math.Abs(lapse) < 1e-12) {

            return basePressure * Math.Exp(-exponent * (geopotential - baseAltitude) / baseTemperature);

        }

        return basePressure * Math.Pow(baseTemperature / temperature, exponent / lapse);

    }

}
