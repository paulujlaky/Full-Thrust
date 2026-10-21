using Brutal.Numerics;
using FullThrust.Core.Craft;
using FullThrust.Core.Ecs;
using FullThrust.Core.Parts;
using FullThrust.Core.Space;

namespace FullThrust.Game.Rendering.Volumetrics;

// Plasma around craft entering an atmosphere fast: a glowing bow shock ahead, a wake behind, and the light they throw on the hull.
public sealed class ReentrySource : IVolumeSource {

    // Sutton-Graves constant for air: heat flux in W/m2 from sqrt(density / nose radius) * speed^3.
    private const double SuttonGraves = 1.83e-4;

    // Heat flux where the shock layer starts to glow, and where it is at its brightest.
    private const double GlowFlux = 4e4;
    private const double PeakFlux = 2e6;

    // Radiance of the shock layer at peak heating, in cd/m2.
    private const float PeakLuminance = 120f;

    private static readonly float3 Dim = new(1.0f, 0.36f, 0.12f);
    private static readonly float3 Hot = new(1.0f, 0.72f, 0.66f);

    private readonly World _world;
    private readonly FloatingOrigin _origin;
    private readonly PointLights _lights;

    public ReentrySource(World world, FloatingOrigin origin, PointLights lights) {

        _world = world;
        _origin = origin;
        _lights = lights;

    }

    public bool Visible { get; set; } = true;

    public int Count { get; private set; }

    // Strongest glow last collected, 0 to 1.
    public float Strength { get; private set; }

    public void Collect(VolumeList volumes) {

        Count = 0;
        Strength = 0;

        foreach (var row in _world.Query<FlightState, Transform>()) {

            ref readonly var flight = ref row.C1;
            ref readonly var craft = ref row.C2;

            var speed = flight.AirVelocity.Length();

            if (speed < 1000 || flight.Ambient.Density <= 0) {

                continue;

            }

            var upstream = flight.AirVelocity / speed;

            Measure(row.Entity, craft, upstream, out var front, out var back, out var nose, out var head, out var tail);

            var flux = SuttonGraves * Math.Sqrt(flight.Ambient.Density / nose) * speed * speed * speed;
            var strength = (float)Math.Clamp(Math.Log(flux / GlowFlux) / Math.Log(PeakFlux / GlowFlux), 0, 1);

            if (strength <= 0) {

                continue;

            }

            Strength = MathF.Max(Strength, strength);

            var colour = float3.Lerp(Dim, Hot, strength);
            var luminance = PeakLuminance * strength * strength;
            var downstream = -SpaceMath.ToFloat(upstream);

            // One shock wraps the whole windward line, so a capsule glows at its shield and a broadside stack wears a single sheath.
            var radius = (float)nose * 1.3f;
            var length = radius * 5;
            var standoff = upstream * (radius * 0.2);

            var nearEnd = _origin.ToLocal(head + standoff);
            var farEnd = _origin.ToLocal(tail + standoff);
            var span = farEnd - nearEnd;

            // The far end of a slanted line sits in the nose's wake and heats less; side-on the whole line takes the flow.
            var across = span - downstream * float3.Dot(span, downstream);
            var broadside = across.Length() / MathF.Max(span.Length(), 1e-3f);
            var farGlow = 0.25f + 0.75f * broadside * broadside;

            var shock = volumes.Add(new VolumePrimitive {

                Kind = new int4((int)VolumeKind.Shock, 16, 1, 0),
                Position = new float4((nearEnd + farEnd) * 0.5f + downstream * (length * 0.5f), span.Length() * 0.5f + MathF.Sqrt(length * length * 0.25f + 2 * radius * length) + radius),
                Axis = new float4(downstream, length),
                Medium = new float4(0, 0, 0, 0),
                Emission = new float4(colour * (luminance / (radius * 0.5f)), 0),
                Params0 = new float4(nearEnd, radius),
                Params1 = new float4(farEnd, farGlow),

            });

            if (shock) {

                Count++;

            }


            var wakeStart = _origin.ToLocal(craft.Position - upstream * back);
            var wakeRadius = (float)nose;
            var wakeLength = wakeRadius * (6 + 20 * strength);

            var wake = volumes.Add(new VolumePrimitive {

                Kind = new int4((int)VolumeKind.Plume, 24, 1, 0),
                Position = new float4(wakeStart + downstream * (wakeLength * 0.5f), MathF.Sqrt(wakeLength * wakeLength * 0.25f + wakeRadius * wakeRadius * 4)),
                Axis = new float4(downstream, wakeLength),
                Medium = new float4(0.5f, 0.5f, 0.5f, 0),
                Emission = new float4(colour * (luminance * 0.5f / (2 * wakeRadius)), 0.3f),
                Params0 = new float4(wakeStart, wakeRadius),
                Params1 = new float4(0.06f, 1, 0, 0),
                Params2 = new float4(1.0f, 0.55f, 0.5f, 0.15f),

            });

            if (wake) {

                Count++;

            }

            // The glowing gas just ahead of the craft, as seen by the hull.
            var glow = _origin.ToLocal(craft.Position + upstream * front);

            _lights.Add(glow, (float)nose * 2, colour * (luminance * MathF.PI * (float)(nose * nose) * 4));

        }

    }

    // How far the craft reaches upstream and downstream of its origin along the flow, its largest part radius, and the ends of its windward line.
    private void Measure(Entity entity, in Transform craft, double3 upstream, out double front, out double back, out double nose, out double3 head, out double3 tail) {

        front = double.MinValue;
        back = 1;
        nose = 0.5;
        head = craft.Position;
        tail = craft.Position;

        // The line runs along the craft's axis as seen by the flow; a craft flying straight into it has a single point.
        var axis = craft.Rotation * double3.UnitY;
        var across = axis - upstream * double3.Dot(axis, upstream);
        var reach = across.Length();

        across = reach > 0.05 ? across / reach : double3.Zero;

        var low = double.MaxValue;
        var high = double.MinValue;
        var foremost = double.MinValue;

        foreach (var row in _world.Query<Part>()) {

            ref readonly var part = ref row.C1;

            if (part.Craft == entity) {

                nose = Math.Max(nose, part.Definition.Radius);

            }

        }

        foreach (var row in _world.Query<Part>()) {

            ref readonly var part = ref row.C1;

            if (part.Craft != entity) {

                continue;

            }

            var leading = Leading(part, craft, upstream, out var offset);

            front = Math.Max(front, leading);
            back = Math.Max(back, Leading(part, craft, -upstream, out _));

            // Thrusters, fins and other small parts sit in the main body's shock rather than setting its ends.
            if (part.Definition.Radius < nose * 0.5) {

                continue;

            }

            var partAxis = craft.Rotation * (part.LocalRotation * double3.UnitY);
            var side = Math.Abs(double3.Dot(partAxis, across));
            var extent = side * part.Definition.Length * 0.5 + (1 - side) * part.Definition.Radius;
            var centre = double3.Dot(offset, across);

            // The part's most upstream point, level with its centre across the flow.
            var windward = craft.Position + offset + upstream * (leading - double3.Dot(offset, upstream));

            if (reach <= 0.05 && leading > foremost) {

                foremost = leading;
                head = windward;
                tail = windward;

            }

            if (reach > 0.05 && centre - extent < low) {

                low = centre - extent;
                head = windward - across * extent;

            }

            if (reach > 0.05 && centre + extent > high) {

                high = centre + extent;
                tail = windward + across * extent;

            }

        }

        front = front == double.MinValue ? 1 : front;

        if (double3.Dot(tail - head, upstream) > 0) {

            (head, tail) = (tail, head);

        }

    }

    // How far a part reaches along a direction from the craft origin; offset is its centre from that origin.
    private static double Leading(in Part part, in Transform craft, double3 direction, out double3 offset) {

        offset = craft.Rotation * part.LocalPosition;

        var axis = craft.Rotation * (part.LocalRotation * double3.UnitY);
        var tilt = Math.Abs(double3.Dot(axis, direction));

        return double3.Dot(offset, direction) + tilt * part.Definition.Length * 0.5 + (1 - tilt) * part.Definition.Radius;

    }

}
