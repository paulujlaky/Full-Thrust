using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Ecs;
using FullThrust.Core.Space;

namespace FullThrust.Game.Rendering.Volumetrics;

internal sealed class GroundSteam(CelestialSystem system, FloatingOrigin origin) {

    private readonly Puff[] _puffs = new Puff[16];
    private int _next;
    private int _sequence;

    private struct Puff {

        public Entity Engine;
        public int Body;
        public double Birth;
        public double3 Surface;
        public double3 Up;
        public double3 Outward;
        public double3 Wind;
        public float Size;
        public float Strength;
        public float Phase;

    }

    public void Clear() {

        Array.Clear(_puffs);
        _next = 0;
        _sequence = 0;

    }

    public void Emit(Entity engine, int body, double3 impact, float radius, float strength, double now) {

        if (strength < 0.09f) {

            return;

        }

        foreach (ref readonly var puff in _puffs.AsSpan()) {

            if (puff.Engine == engine && now - puff.Birth < 0.64) {

                return;

            }

        }

        var anchor = system.WorldToBodyFixed(body, impact, now);
        var up = double3.Normalize(anchor);
        var east = double3.Normalize(double3.Cross(Math.Abs(up.Y) < 0.99 ? double3.UnitY : double3.UnitZ, up));
        var north = double3.Cross(up, east);
        var angle = ++_sequence * 2.399963;
        _puffs[_next] = new Puff {

            Engine = engine,
            Body = body,
            Birth = now,
            Surface = anchor,
            Up = up,
            Outward = east * Math.Cos(angle) + north * Math.Sin(angle),
            Wind = east * 3.7 + north * 1.1,
            Size = radius,
            Strength = strength,
            Phase = (float)(angle % 1),

        };
        _next = (_next + 1) % _puffs.Length;

    }

    public void Collect(VolumeList volumes, double now) {

        foreach (ref var puff in _puffs.AsSpan()) {

            if (puff.Engine.IsNull) {

                continue;

            }

            var age = now - puff.Birth;
            if (age < 0 || age >= 16) {

                puff.Engine = Entity.Null;
                continue;

            }

            var expansion = Math.Sqrt(age + 0.03);
            var radius = puff.Size * (1.3f + 3.9f * (float)expansion);
            var fade = (float)(1 - age / 16);
            var sweep = puff.Size * (4.5 + 12 * (1 - Math.Exp(-age * 0.8)));
            var center = puff.Surface + puff.Outward * sweep + puff.Wind * age + puff.Up * (radius * 0.46 + 0.29 * age * age);
            var position = origin.ToLocal(system.BodyFixedToWorld(puff.Body, center, now).Position);
            volumes.Add(new VolumePrimitive {

                Kind = new int4((int)VolumeKind.NoiseSphere, 20, 3, 1),
                Position = new float4(position, radius),
                Medium = new float4(0.94f, 0.94f, 0.93f, 0.53f * puff.Strength * fade * fade / (0.65f + 0.26f * (float)age)),
                Emission = new float4(0, 0, 0, 0.42f),
                Params0 = new float4(radius * 1.35f, puff.Phase, (float)age * 0.027f, 0.75f - puff.Phase),

            });

        }

    }

}
