using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Craft;
using FullThrust.Core.Ecs;
using FullThrust.Core.Parts;
using FullThrust.Core.Space;

namespace FullThrust.Game.Rendering.Volumetrics;

// Smoke that engines leave in dense air: capsules fixed to the ground below, drifting on a sheared wind, spreading and thinning for minutes.
public sealed class TrailSource : IVolumeSource {

    private const int MaxSegments = 32;
    private const int MaxCraft = 8;

    // TrailNoiseScale in Shaders/volumetric.glsl.
    private const double NoiseScale = 40.0;

    private const double SegmentSeconds = 1.5;
    private const double MaxSegmentLength = 600;
    private const double Lifetime = 300;

    // Turbulent spreading: the radius grows with the square root of age.
    private const double Spread = 8;

    // Below this ambient pressure the exhaust expands too fast to leave a visible column.
    private const double MinPressure = 2000;
    private const double FullPressure = 30000;

    private readonly World _world;
    private readonly CelestialSystem _system;
    private readonly FloatingOrigin _origin;
    private readonly PlumeSource _plumes;

    private readonly Segment[] _segments = new Segment[MaxSegments];
    private readonly Entity[] _craft = new Entity[MaxCraft];
    private readonly int[] _open = new int[MaxCraft];

    private double _time;

    public TrailSource(World world, CelestialSystem system, FloatingOrigin origin, PlumeSource plumes) {

        _world = world;
        _system = system;
        _origin = origin;
        _plumes = plumes;

        Array.Fill(_open, -1);

    }

    public bool Visible { get; set; } = true;

    public int Count { get; private set; }

    // Lays down this frame's smoke; call once per frame before rendering.
    public void Update(double universalTime) {

        _time = universalTime;

        for (var i = 0; i < MaxSegments; i++) {

            if (_segments[i].Live && _time - _segments[i].EndTime > Lifetime) {

                _segments[i].Live = false;

            }

        }

        Span<bool> emitting = stackalloc bool[MaxCraft];

        var transforms = _world.Store<Transform>();
        var flights = _world.Store<FlightState>();

        foreach (var row in _world.Query<EngineState, Part>()) {

            ref readonly var engine = ref row.C1;
            ref readonly var part = ref row.C2;

            if (!engine.IsBurning || engine.Pump <= 0.01 || !flights.Has(part.Craft) || _plumes.StyleFor(engine.Definition.Fuel) is not { Trail: > 0 } style) {

                continue;

            }

            ref readonly var flight = ref flights.Get(part.Craft);

            if (flight.Ambient.Pressure < MinPressure) {

                continue;

            }

            var slot = SlotOf(part.Craft);

            if (slot < 0 || emitting[slot]) {

                continue;

            }

            emitting[slot] = true;

            ref readonly var craft = ref transforms.Get(part.Craft);

            var exitRadius = Math.Sqrt(Math.Max(engine.Definition.ExitArea, 1e-4) / Math.PI);
            var axis = -double3.NormalizeOrZero(craft.Rotation * engine.ThrustDirection);
            var exit = craft.Position + craft.Rotation * (part.LocalPosition - part.LocalRotation * new double3(0, part.Definition.Length / 2, 0));

            // The column starts where the luminous plume has mixed into the air.
            var source = _system.WorldToBodyFixed(flight.Body, exit + axis * (exitRadius * 25), _time);
            var thickness = Math.Clamp((flight.Ambient.Pressure - MinPressure) / (FullPressure - MinPressure), 0, 1);

            // Exhaust balloons out into thin air, so the column is born wider the higher it is laid.
            var expansion = Math.Min(Math.Sqrt(PhysicalConstants.SeaLevelPressure / flight.Ambient.Pressure), 6);

            Emit(slot, flight.Body, source, Math.Max(exitRadius * 8, part.Definition.Radius * 2.5) * expansion, style.Trail * thickness * engine.Pump / expansion, style.TrailAlbedo);

        }

        for (var slot = 0; slot < MaxCraft; slot++) {

            if (!emitting[slot] && _open[slot] >= 0) {

                _open[slot] = -1;
                _craft[slot] = Entity.Null;

            }

        }

    }

    private void Emit(int slot, int body, double3 source, double radius, double extinction, double[] albedo) {

        var open = _open[slot];

        if (open >= 0) {

            ref var segment = ref _segments[open];

            segment.End = source;
            segment.EndTime = _time;
            segment.EndDrift = Wind(_system[body], source);
            segment.Extinction = extinction;

            if (_time - segment.StartTime < SegmentSeconds && (segment.End - segment.Start).Length() < MaxSegmentLength) {

                return;

            }

        }

        var next = Allocate();

        _segments[next] = new Segment {

            Live = true,
            Body = body,

            Start = source,
            End = source,
            StartTime = _time,
            EndTime = _time,
            StartDrift = Wind(_system[body], source),
            EndDrift = Wind(_system[body], source),

            BirthRadius = radius,
            Extinction = extinction,
            Albedo = new float3((float)albedo[0], (float)albedo[1], (float)albedo[2]),

        };

        _open[slot] = next;

    }

    // A free segment, or else the one that ended longest ago.
    private int Allocate() {

        var oldest = 0;

        for (var i = 0; i < MaxSegments; i++) {

            if (!_segments[i].Live) {

                return i;

            }

            if (_segments[i].EndTime < _segments[oldest].EndTime && !IsOpen(i)) {

                oldest = i;

            }

        }

        return oldest;

    }

    private bool IsOpen(int segment) {

        for (var slot = 0; slot < MaxCraft; slot++) {

            if (_open[slot] == segment) {

                return true;

            }

        }

        return false;

    }

    private int SlotOf(Entity craft) {

        var free = -1;

        for (var slot = 0; slot < MaxCraft; slot++) {

            if (_craft[slot] == craft) {

                return slot;

            }

            if (free < 0 && _craft[slot] == Entity.Null) {

                free = slot;

            }

        }

        if (free >= 0) {

            _craft[free] = craft;
            _open[free] = -1;

        }

        return free;

    }

    // Prevailing winds strengthen towards a jet stream near 12 km and veer with height, so the column twists as it drifts.
    private static double3 Wind(CelestialBody body, double3 bodyFixed) {

        var up = double3.Normalize(bodyFixed);
        var east = double3.NormalizeOrZero(double3.Cross(double3.UnitY, up));
        var north = double3.Cross(up, east);
        var height = bodyFixed.Length() - body.Radius;
        var jet = Math.Clamp(height / 12000, 0, 1);

        return east * (4 + 26 * jet * jet * (3 - 2 * jet)) + north * (3 * Math.Sin(height / 2500));

    }

    public void Collect(VolumeList volumes) {

        Count = 0;

        if (!Visible) {

            return;

        }

        for (var i = 0; i < MaxSegments; i++) {

            ref readonly var segment = ref _segments[i];

            if (!segment.Live) {

                continue;

            }

            var startAge = _time - segment.StartTime;
            var endAge = _time - segment.EndTime;

            var startFixed = segment.Start + segment.StartDrift * startAge;
            var endFixed = segment.End + segment.EndDrift * endAge;

            var startRadius = Radius(segment.BirthRadius, startAge);
            var endRadius = Radius(segment.BirthRadius, endAge);

            var start = _origin.ToLocal(_system.BodyFixedToWorld(segment.Body, startFixed, _time).Position);
            var end = _origin.ToLocal(_system.BodyFixedToWorld(segment.Body, endFixed, _time).Position);

            var turn = _system[segment.Body].RotationAt(_time);
            var noise = startFixed / NoiseScale;

            var added = volumes.Add(new VolumePrimitive {

                Kind = new int4((int)VolumeKind.Trail, 12, 3, 0),
                Position = new float4((start + end) * 0.5f, (end - start).Length() * 0.5f + (float)Math.Max(startRadius, endRadius) * 1.3f),
                Axis = new float4((float)-turn.X, (float)-turn.Y, (float)-turn.Z, (float)turn.W),
                Medium = new float4(segment.Albedo, (float)Extinction(segment, startAge, startRadius)),
                Emission = new float4(0, 0, 0, 0.5f),
                Params0 = new float4(start, (float)startRadius),
                Params1 = new float4(end, (float)endRadius),
                Params2 = new float4((float)Fraction(noise.X), (float)Fraction(noise.Y), (float)Fraction(noise.Z), (float)Extinction(segment, endAge, endRadius)),

            });

            if (added) {

                Count++;

            }

        }

    }

    private static double Radius(double birth, double age) => birth + Spread * Math.Sqrt(Math.Max(age, 0));

    // The smoke in a slice of column spreads over its growing cross-section, then fades as it disperses.
    private static double Extinction(in Segment segment, double age, double radius) {

        var area = segment.BirthRadius / radius;
        var fade = 1 - Math.Clamp((age - Lifetime * 0.5) / (Lifetime * 0.5), 0, 1);

        return segment.Extinction * area * area * fade * fade;

    }

    private static double Fraction(double value) => value - Math.Floor(value);

    private struct Segment {

        public bool Live;
        public int Body;

        // Body-fixed metres where each end was laid down, and the wind each end drifts on.
        public double3 Start;
        public double3 End;
        public double3 StartDrift;
        public double3 EndDrift;

        public double StartTime;
        public double EndTime;

        public double BirthRadius;
        public double Extinction;
        public float3 Albedo;

    }

}
