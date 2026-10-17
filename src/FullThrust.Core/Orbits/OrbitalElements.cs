using Brutal.Numerics;
using FullThrust.Core.Simulation;

namespace FullThrust.Core.Orbits;

// Angles in radians. The reference plane is the world XZ plane with +Y as north and +X as the node reference.
public struct OrbitalElements {

    public double SemiMajorAxis;
    public double Eccentricity;

    public double Inclination;
    public double LongitudeOfAscendingNode;
    public double ArgumentOfPeriapsis;

    public double TrueAnomaly;

    public readonly bool IsClosed => Eccentricity < 1.0;

    public readonly double Periapsis => SemiMajorAxis * (1 - Eccentricity);

    public readonly double Apoapsis => IsClosed ? SemiMajorAxis * (1 + Eccentricity) : double.PositiveInfinity;

    public readonly double Period(double mu) => IsClosed ? 2 * Math.PI * Math.Sqrt(SemiMajorAxis * SemiMajorAxis * SemiMajorAxis / mu) : double.PositiveInfinity;

    public static OrbitalElements FromState(in StateVector state, double mu) {

        var r = OrbitFrame.ToZUp(state.Position);
        var v = OrbitFrame.ToZUp(state.Velocity);

        var rMag = r.Length();
        var vMag = v.Length();

        var h = double3.Cross(r, v);
        var hMag = h.Length();

        var node = new double3(-h.Y, h.X, 0);
        var nodeMag = node.Length();

        var eVec = ((vMag * vMag - mu / rMag) * r - double3.Dot(r, v) * v) / mu;
        var e = eVec.Length();

        var energy = vMag * vMag / 2 - mu / rMag;
        var a = Math.Abs(e - 1) < 1e-12 ? double.PositiveInfinity : -mu / (2 * energy);

        var elements = new OrbitalElements {

            SemiMajorAxis = a,
            Eccentricity = e,
            Inclination = Math.Acos(Math.Clamp(h.Z / hMag, -1, 1)),

        };

        const double Small = 1e-10;

        var equatorial = nodeMag < Small * hMag;
        var circular = e < Small;

        elements.LongitudeOfAscendingNode = equatorial ? 0 : Angle(node.X / nodeMag, node.Y);

        if (circular) {

            elements.ArgumentOfPeriapsis = 0;

            // Measure from the node (or +X when equatorial) since periapsis is undefined.
            var reference = equatorial ? double3.UnitX : node / nodeMag;
            var cos = double3.Dot(reference, r) / rMag;
            var along = double3.Dot(double3.Cross(reference, r), h);

            elements.TrueAnomaly = Angle(cos, along);

        } else {

            if (equatorial) {

                var longitude = Math.Atan2(eVec.Y, eVec.X);

                elements.ArgumentOfPeriapsis = h.Z >= 0 ? Wrap(longitude) : Wrap(-longitude);

            } else {

                elements.ArgumentOfPeriapsis = Angle(double3.Dot(node, eVec) / (nodeMag * e), eVec.Z);

            }

            elements.TrueAnomaly = Angle(double3.Dot(eVec, r) / (e * rMag), double3.Dot(r, v));

        }

        return elements;

    }

    public readonly StateVector ToState(double mu) {

        var e = Eccentricity;
        var p = double.IsPositiveInfinity(SemiMajorAxis) ? 0 : SemiMajorAxis * (1 - e * e);

        var nu = TrueAnomaly;
        var cosNu = Math.Cos(nu);
        var sinNu = Math.Sin(nu);

        var r = p / (1 + e * cosNu);

        var position = new double3(r * cosNu, r * sinNu, 0);
        var velocity = new double3(-sinNu, e + cosNu, 0) * Math.Sqrt(mu / p);

        position = RotatePerifocal(position);
        velocity = RotatePerifocal(velocity);

        return new StateVector(OrbitFrame.FromZUp(position), OrbitFrame.FromZUp(velocity));

    }

    public static double TrueFromMeanAnomaly(double meanAnomaly, double eccentricity) {

        var m = Wrap(meanAnomaly);
        var e = eccentricity;
        var ecc = e < 0.8 ? m : Math.PI;

        for (var i = 0; i < 50; i++) {

            var step = (ecc - e * Math.Sin(ecc) - m) / (1 - e * Math.Cos(ecc));

            ecc -= step;

            if (Math.Abs(step) < 1e-14) {

                break;

            }

        }

        return 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(ecc / 2), Math.Sqrt(1 - e) * Math.Cos(ecc / 2));

    }

    private readonly double3 RotatePerifocal(double3 v) {

        var cosO = Math.Cos(LongitudeOfAscendingNode);
        var sinO = Math.Sin(LongitudeOfAscendingNode);

        var cosI = Math.Cos(Inclination);
        var sinI = Math.Sin(Inclination);

        var cosW = Math.Cos(ArgumentOfPeriapsis);
        var sinW = Math.Sin(ArgumentOfPeriapsis);

        var x = (cosO * cosW - sinO * sinW * cosI) * v.X + (-cosO * sinW - sinO * cosW * cosI) * v.Y;
        var y = (sinO * cosW + cosO * sinW * cosI) * v.X + (-sinO * sinW + cosO * cosW * cosI) * v.Y;
        var z = sinW * sinI * v.X + cosW * sinI * v.Y;

        return new double3(x, y, z);

    }

    private static double Angle(double cosine, double sign) {

        var angle = Math.Acos(Math.Clamp(cosine, -1, 1));

        return sign < 0 ? 2 * Math.PI - angle : angle;

    }

    private static double Wrap(double angle) {

        angle %= 2 * Math.PI;

        return angle < 0 ? angle + 2 * Math.PI : angle;

    }

}

public static class OrbitFrame {

    // Game space is Y-up; textbook orbital formulas are Z-up.
    public static double3 ToZUp(double3 v) => new(v.X, -v.Z, v.Y);

    public static double3 FromZUp(double3 v) => new(v.X, v.Z, -v.Y);

    public static readonly double3 North = double3.UnitY;

}
