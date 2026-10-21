using System.Text.Json;
using Brutal.Numerics;
using FullThrust.Core.Craft;
using FullThrust.Core.Ecs;
using FullThrust.Core.Parts;
using FullThrust.Core.Space;

namespace FullThrust.Game.Rendering.Volumetrics;

public sealed class PlumeStyleData {

    public string Fuel { get; set; } = "";

    public double[] Core { get; set; } = [1, 0.6, 0.3];

    public double Brightness { get; set; } = 1;

    public double Soot { get; set; }

    public double[] Albedo { get; set; } = [0.5, 0.5, 0.5];

    // Colour the cooler outer sheath shifts towards, as a multiplier of the core colour.
    public double[] Sheath { get; set; } = [1.0, 0.55, 0.3];

    // How far the hot core near the nozzle washes out towards white.
    public double CoreWhiteness { get; set; } = 0.6;

}

public sealed class PlumeStylesData {

    public List<PlumeStyleData> Styles { get; set; } = [];

}

// Exhaust plumes for every burning engine, shaped by its nozzle and the ambient pressure.
public sealed class PlumeSource : IVolumeSource, IFrameUniformsWriter {

    // Luminance of a brightness-1 plume core, in cd/m2; kerosene afterburning is about this bright.
    private const float CoreLuminance = 4.0e5f;

    private const float PuffLength = 2.4f;

    private static readonly JsonSerializerOptions Options = new() {

        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,

    };

    private readonly World _world;
    private readonly FloatingOrigin _origin;

    // Indexed by fuel resource; null where no style is defined.
    private readonly PlumeStyleData?[] _styles;

    private readonly float4[] _lightPositions = new float4[LightVectors.MaxPointLights];
    private readonly float4[] _lightIntensities = new float4[LightVectors.MaxPointLights];

    private int _lightCount;

    public PlumeSource(World world, FloatingOrigin origin, PartCatalog parts, string stylesFile) {

        _world = world;
        _origin = origin;

        var data = JsonSerializer.Deserialize<PlumeStylesData>(File.ReadAllText(stylesFile), Options) ?? new PlumeStylesData();

        _styles = new PlumeStyleData?[parts.Resources.Count];

        for (var i = 0; i < _styles.Length; i++) {

            _styles[i] = data.Styles.Find(s => string.Equals(s.Fuel, parts.Resources[i].Name, StringComparison.OrdinalIgnoreCase));

        }

    }

    public bool Visible { get; set; } = true;

    public int Count { get; private set; }

    public void Collect(VolumeList volumes) {

        var transforms = _world.Store<Transform>();
        var flights = _world.Store<FlightState>();

        Count = 0;
        _lightCount = 0;

        foreach (var row in _world.Query<EngineState, Part>()) {

            ref readonly var engine = ref row.C1;
            ref readonly var part = ref row.C2;

            if (!engine.IsBurning || engine.Pump <= 0.01 || engine.Definition.Fuel < 0 || engine.Definition.Fuel >= _styles.Length || _styles[engine.Definition.Fuel] is not { } style) {

                continue;

            }

            ref readonly var craft = ref transforms.Get(part.Craft);

            var flight = flights.Has(part.Craft) ? flights.Get(part.Craft) : default;

            var exit = craft.Position + craft.Rotation * (part.LocalPosition - part.LocalRotation * new double3(0, part.Definition.Length / 2, 0));
            var axis = -double3.NormalizeOrZero(craft.Rotation * engine.ThrustDirection);
            var plume = Describe(engine, part.Definition, style, _origin.ToLocal(exit), SpaceMath.ToFloat(axis), (float)flight.Ambient.Pressure);

            if (!volumes.Add(plume)) {

                continue;

            }

            Count++;

            var exitRadius = plume.Params0.W;
            var length = plume.Axis.W;

            // The brightest third of the plume, seen side on, as a point light at its middle.
            if (_lightCount < LightVectors.MaxPointLights) {

                var luminance = new float3(plume.Emission.X, plume.Emission.Y, plume.Emission.Z) * (2 * exitRadius);
                var position = plume.Params0.XYZ + plume.Axis.XYZ * (length * 0.15f);

                _lightPositions[_lightCount] = new float4(position, exitRadius * 2);
                _lightIntensities[_lightCount] = new float4(luminance * (length * 0.3f * 2 * exitRadius), 0);
                _lightCount++;

            }

            AddSteam(volumes, flight, craft.Position, exit, axis, exitRadius, length, (float)engine.Pump);

        }

        AddPuffs(volumes, transforms);

    }

    // Firing RCS nozzles vent a short cone of propellant mist, seen by the sunlight it scatters.
    private void AddPuffs(VolumeList volumes, ComponentStore<Transform> transforms) {

        foreach (var row in _world.Query<RcsState, Part>()) {

            ref readonly var rcs = ref row.C1;

            if (rcs.Firing == 0) {

                continue;

            }

            ref readonly var part = ref row.C2;
            ref readonly var craft = ref transforms.Get(part.Craft);

            var nozzles = rcs.Definition.Nozzles;

            for (var n = 0; n < nozzles.Length && n < 32; n++) {

                if ((rcs.Firing & (1u << n)) == 0) {

                    continue;

                }

                var exhaust = -double3.Normalize(craft.Rotation * (part.LocalRotation * nozzles[n]));
                var exit = _origin.ToLocal(craft.Position + craft.Rotation * part.LocalPosition + exhaust * (part.Definition.Radius * 0.8));
                var axis = SpaceMath.ToFloat(exhaust);

                volumes.Add(new VolumePrimitive {

                    Kind = new int4((int)VolumeKind.Plume, 16, 2, 0),
                    Position = new float4(exit + axis * (PuffLength * 0.5f), PuffLength),
                    Axis = new float4(axis, PuffLength),
                    Medium = new float4(0.92f, 0.92f, 0.92f, 70),
                    Emission = new float4(0, 0, 0, 0.5f),
                    Params0 = new float4(exit, 0.05f),
                    Params1 = new float4(0.5f, 1, 0, 0),
                    Params2 = new float4(1, 1, 1, 0),

                });

            }

        }

    }

    // Where the exhaust strikes the ground it throws up a cloud of steam and dust, lit by the plume.
    private void AddSteam(VolumeList volumes, in FlightState flight, double3 craftPosition, double3 exit, double3 axis, float exitRadius, float length, float throttle) {

        if (flight.Gravity == double3.Zero) {

            return;

        }

        var up = -double3.Normalize(flight.Gravity);
        var down = -double3.Dot(axis, up);
        var height = flight.SurfaceAltitude + double3.Dot(exit - craftPosition, up);

        if (down < 0.3 || height <= 0 || height > length * 0.8) {

            return;

        }

        var impact = exit + axis * (height / down);
        var radius = exitRadius * (4 + 10 * (1 - (float)(height / length))) * throttle;
        var center = impact + up * (radius * 0.4);

        volumes.Add(new VolumePrimitive {

            Kind = new int4((int)VolumeKind.NoiseSphere, 32, 4, 0),
            Position = new float4(_origin.ToLocal(center), radius),
            Axis = new float4(SpaceMath.ToFloat(up), 0),
            Medium = new float4(0.8f, 0.78f, 0.75f, 0.12f),
            Emission = new float4(0, 0, 0, 0.3f),
            Params0 = new float4(radius * 0.6f, 0.37f, 0.11f, 0.73f),

        });

    }

    public void Fill(ref FrameUniforms uniforms, bool enabled) {

        var count = enabled && Visible ? _lightCount : 0;

        for (var i = 0; i < count; i++) {

            uniforms.LightPositions[i] = _lightPositions[i];
            uniforms.LightIntensities[i] = _lightIntensities[i];

        }

        uniforms.LightCount = new float4(count, 0, 0, 0);

    }

    private static VolumePrimitive Describe(in EngineState engine, PartDefinition part, PlumeStyleData style, float3 exit, float3 axis, float ambient) {

        var definition = engine.Definition;
        var throttle = (float)Math.Clamp(engine.Pump, 0, 1);

        var exitRadius = (float)Math.Min(Math.Sqrt(Math.Max(definition.ExitArea, 1e-4) / Math.PI), part.Radius * 0.95);

        // Nozzles designed for sea level exhaust near ambient pressure; vacuum nozzles expand far further.
        var isp = (float)(definition.SeaLevelIsp / Math.Max(definition.VacuumIsp, 1));
        var exitPressure = (float)PhysicalConstants.SeaLevelPressure * Math.Clamp(0.75f * MathF.Pow(isp, 6), 0.002f, 0.8f);
        var ratio = exitPressure / MathF.Max(ambient, 1.0f);

        var spread = ratio > 1 ? Math.Clamp(0.04f + 0.11f * MathF.Log(ratio), 0.04f, 1.2f) : 0.02f;
        var thin = 1 - Math.Clamp(ambient / (0.3f * (float)PhysicalConstants.SeaLevelPressure), 0, 1);
        var length = exitRadius * (20 + 40 * throttle) * (1 + 2.5f * thin);

        // Shock diamonds stand in dense air, spaced by the exit diameter and how far the flow is from matching ambient.
        var diamondSpacing = 2 * exitRadius * 1.3f * MathF.Sqrt(MathF.Max(ratio, 0.2f));
        var diamonds = Math.Clamp((ambient - 5000) / 25000, 0, 1);

        var widest = exitRadius + length * spread;
        var center = exit + axis * (length * 0.5f);
        var bound = MathF.Sqrt(length * length * 0.25f + widest * widest);

        var core = new float3((float)style.Core[0], (float)style.Core[1], (float)style.Core[2]);
        var emission = core * (float)(style.Brightness * CoreLuminance * throttle / (2 * exitRadius));

        return new VolumePrimitive {

            Kind = new int4((int)VolumeKind.Plume, 32, 2, 0),
            Position = new float4(center, bound),
            Axis = new float4(axis, length),
            Medium = new float4((float)style.Albedo[0], (float)style.Albedo[1], (float)style.Albedo[2], (float)style.Soot * throttle / exitRadius),
            Emission = new float4(emission, 0.3f),
            Params0 = new float4(exit, exitRadius),
            Params1 = new float4(spread, diamondSpacing, diamonds, 0),
            Params2 = new float4((float)style.Sheath[0], (float)style.Sheath[1], (float)style.Sheath[2], (float)style.CoreWhiteness),

        };

    }

}
