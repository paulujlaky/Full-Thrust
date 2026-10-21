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

            Measure(row.Entity, craft, upstream, out var front, out var back, out var nose);

            var flux = SuttonGraves * Math.Sqrt(flight.Ambient.Density / nose) * speed * speed * speed;
            var strength = (float)Math.Clamp(Math.Log(flux / GlowFlux) / Math.Log(PeakFlux / GlowFlux), 0, 1);

            if (strength <= 0) {

                continue;

            }

            Strength = MathF.Max(Strength, strength);

            var colour = float3.Lerp(Dim, Hot, strength);
            var luminance = PeakLuminance * strength * strength;
            var downstream = -SpaceMath.ToFloat(upstream);

            // Each part facing the flow carries its own cap, so a tumbling stack glows along its windward side, a capsule just at its shield.
            foreach (var parts in _world.Query<Part>()) {

                ref readonly var part = ref parts.C1;

                if (part.Craft != row.Entity) {

                    continue;

                }

                var leading = Leading(part, craft, upstream, out var offset);
                var exposure = (float)Math.Exp(-(front - leading) / (2 * nose));

                if (exposure < 0.05f) {

                    continue;

                }

                var radius = (float)part.Definition.Radius * 1.3f;
                var length = radius * 5;

                // The shock stands off the leading face by a fraction of its radius.
                var stagnation = _origin.ToLocal(craft.Position + offset + upstream * (leading - double3.Dot(offset, upstream) + radius * 0.2));

                var added = volumes.Add(new VolumePrimitive {

                    Kind = new int4((int)VolumeKind.Shock, 16, 1, 0),
                    Position = new float4(stagnation + downstream * (length * 0.5f), MathF.Sqrt(length * length * 0.25f + 2 * radius * length) + radius),
                    Axis = new float4(downstream, length),
                    Medium = new float4(0, 0, 0, 0),
                    Emission = new float4(colour * (luminance * exposure / (radius * 0.5f)), 0),
                    Params0 = new float4(stagnation, radius),

                });

                if (added) {

                    Count++;

                }

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

    // How far the craft reaches upstream and downstream of its origin along the flow, and its largest part radius.
    private void Measure(Entity entity, in Transform craft, double3 upstream, out double front, out double back, out double nose) {

        front = double.MinValue;
        back = 1;
        nose = 0.5;

        foreach (var row in _world.Query<Part>()) {

            ref readonly var part = ref row.C1;

            if (part.Craft != entity) {

                continue;

            }

            front = Math.Max(front, Leading(part, craft, upstream, out _));
            back = Math.Max(back, Leading(part, craft, -upstream, out _));
            nose = Math.Max(nose, part.Definition.Radius);

        }

        front = front == double.MinValue ? 1 : front;

    }

    // How far a part reaches along a direction from the craft origin; offset is its centre from that origin.
    private static double Leading(in Part part, in Transform craft, double3 direction, out double3 offset) {

        offset = craft.Rotation * part.LocalPosition;

        var axis = craft.Rotation * (part.LocalRotation * double3.UnitY);
        var tilt = Math.Abs(double3.Dot(axis, direction));

        return double3.Dot(offset, direction) + tilt * part.Definition.Length * 0.5 + (1 - tilt) * part.Definition.Radius;

    }

}
