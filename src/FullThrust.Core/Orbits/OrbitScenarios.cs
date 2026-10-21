using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Simulation;

namespace FullThrust.Core.Orbits;

public static class OrbitScenarios {

    public static StateVector Circular(double mu, double radius, double inclination = 0, double phase = 0) {

        var elements = new OrbitalElements {

            SemiMajorAxis = radius,
            Eccentricity = 0,
            Inclination = inclination,
            TrueAnomaly = phase,

        };

        return elements.ToState(mu);

    }


    // At a radius with a speed and a flight-path angle to the local horizontal, negative descending, e.g. a capsule meeting the upper atmosphere.
    public static StateVector Entry(double mu, double radius, double speed, double flightPathAngle) {

        var circular = Circular(mu, radius);
        var up = double3.Normalize(circular.Position);
        var along = double3.Normalize(circular.Velocity);

        return new StateVector(circular.Position, (along * Math.Cos(flightPathAngle) + up * Math.Sin(flightPathAngle)) * speed);

    }

    // Hohmann-like transfer in the moon's orbital plane, timed so apoapsis meets the moon.
    public static StateVector LunarTransfer(CelestialSystem system, CelestialBody moon, double parkingRadius, double universalTime, double apoapsisOffset = -12_000_000) {

        var planet = moon.Parent ?? throw new ArgumentException("Moon needs a parent body.", nameof(moon));
        var mu = planet.GravitationalParameter;

        var arrival = universalTime + Math.PI * Math.Sqrt(Math.Pow((parkingRadius + moon.Orbit.SemiMajorAxis) / 2, 3) / mu);

        var direction = double3.Zero;
        var normal = double3.Zero;
        var apoapsis = 0.0;

        for (var i = 0; i < 4; i++) {

            var moonState = system.RelativeState(moon, arrival);

            direction = double3.Normalize(moonState.Position);
            normal = double3.Normalize(double3.Cross(moonState.Position, moonState.Velocity));
            apoapsis = moonState.Position.Length() + apoapsisOffset;

            var a = (parkingRadius + apoapsis) / 2;

            arrival = universalTime + Math.PI * Math.Sqrt(a * a * a / mu);

        }

        var periapsisDirection = -direction;
        var prograde = double3.Normalize(double3.Cross(normal, periapsisDirection));
        var speed = Math.Sqrt(mu * 2 * apoapsis / (parkingRadius * (parkingRadius + apoapsis)));

        return new StateVector(periapsisDirection * parkingRadius, prograde * speed);

    }

}
