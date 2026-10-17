using Brutal.Numerics;
using FullThrust.Core.Simulation;

namespace FullThrust.Core.Bodies;

public sealed class CelestialSystem {

    private readonly CelestialBody[] _bodies;

    private readonly double3[] _positions;
    private readonly double3[] _velocities;

    private double _cachedTime = double.NaN;

    public CelestialSystem(string name, CelestialBody[] bodies, IOrbitPropagator propagator, Sun? sun = null) {

        Name = name;
        Propagator = propagator;
        Sun = sun ?? new Sun();

        _bodies = bodies;
        _positions = new double3[bodies.Length];
        _velocities = new double3[bodies.Length];

        Root = bodies[0];

        Update(0);

    }

    public string Name { get; }

    public IOrbitPropagator Propagator { get; }

    public Sun Sun { get; }

    public CelestialBody Root { get; }

    public ReadOnlySpan<CelestialBody> Bodies => _bodies;

    public int Count => _bodies.Length;

    public CelestialBody this[int index] => _bodies[index];

    public double CachedTime => _cachedTime;

    public CelestialBody? Find(string name) {

        foreach (var body in _bodies) {

            if (string.Equals(body.Name, name, StringComparison.OrdinalIgnoreCase)) {

                return body;

            }

        }

        return null;

    }

    // Bodies are ordered parents-first, so one forward pass resolves every chain.
    public void Update(double universalTime) {

        if (universalTime == _cachedTime) {

            return;

        }

        for (var i = 0; i < _bodies.Length; i++) {

            var body = _bodies[i];

            if (body.Parent is null) {

                _positions[i] = double3.Zero;
                _velocities[i] = double3.Zero;

                continue;

            }

            var local = RelativeState(body, universalTime);
            var parent = body.Parent.Index;

            _positions[i] = _positions[parent] + local.Position;
            _velocities[i] = _velocities[parent] + local.Velocity;

        }

        _cachedTime = universalTime;

    }

    public double3 Position(int body) => _positions[body];

    public double3 Velocity(int body) => _velocities[body];

    public StateVector RelativeState(CelestialBody body, double universalTime) {

        if (body.Parent is null) {

            return default;

        }

        return Propagator.Propagate(body.EpochState, body.OrbitGravitationalParameter, universalTime - body.OrbitEpoch);

    }

    public StateVector StateAt(int body, double universalTime) {

        if (universalTime == _cachedTime) {

            return new StateVector(_positions[body], _velocities[body]);

        }

        var state = default(StateVector);

        for (var current = _bodies[body]; current.Parent is not null; current = current.Parent) {

            var local = RelativeState(current, universalTime);

            state.Position += local.Position;
            state.Velocity += local.Velocity;

        }

        return state;

    }

    public StateVector Rebase(in StateVector relative, int fromBody, int toBody, double universalTime) {

        if (fromBody == toBody) {

            return relative;

        }

        var from = StateAt(fromBody, universalTime);
        var to = StateAt(toBody, universalTime);

        return new StateVector(relative.Position + from.Position - to.Position, relative.Velocity + from.Velocity - to.Velocity);

    }

    // Acceleration of a body's non-rotating frame relative to the root, so physics in root space matches patched conics.
    public double3 FrameAcceleration(int body, double universalTime) {

        var acceleration = double3.Zero;

        for (var current = _bodies[body]; current.Parent is not null; current = current.Parent) {

            var r = RelativeState(current, universalTime).Position;
            var distance = r.Length();

            acceleration -= r * (current.OrbitGravitationalParameter / (distance * distance * distance));

        }

        return acceleration;

    }

    // Latitude and longitude in radians, in the body-fixed frame (+Y north).
    public static double3 BodyFixedPoint(CelestialBody body, double latitude, double longitude, double altitude) {

        var radius = body.Radius + altitude;
        var cosLatitude = Math.Cos(latitude);

        return new double3(cosLatitude * Math.Cos(longitude), Math.Sin(latitude), -cosLatitude * Math.Sin(longitude)) * radius;

    }

    public StateVector BodyFixedToWorld(int body, double3 bodyFixed, double universalTime) {

        var celestial = _bodies[body];
        var frame = StateAt(body, universalTime);
        var offset = celestial.RotationAt(universalTime) * bodyFixed;

        return new StateVector(frame.Position + offset, frame.Velocity + double3.Cross(celestial.AngularVelocity, offset));

    }

    public double3 WorldToBodyFixed(int body, double3 world, double universalTime) {

        var offset = world - StateAt(body, universalTime).Position;

        return doubleQuat.Conjugate(_bodies[body].RotationAt(universalTime)) * offset;

    }

    // Height above the terrain (or reference radius without terrain) for a world position.
    public double SurfaceAltitude(int body, double3 world, double universalTime, double resolution = 1.0) {

        var celestial = _bodies[body];
        var bodyFixed = WorldToBodyFixed(body, world, universalTime);
        return bodyFixed.Length() - _bodies[body].Radius - _bodies[body].GroundHeight(bodyFixed, resolution);

    }

    public CelestialBody DominantBodyAt(double3 position, double universalTime) {

        var best = Root;

        // Descend while a child's sphere of influence contains the point.
        var descended = true;

        while (descended) {

            descended = false;

            var children = best.ChildList;

            for (var i = 0; i < children.Count; i++) {

                var child = children[i];
                var childPosition = StateAt(child.Index, universalTime).Position;

                if (double3.DistanceSquared(position, childPosition) < child.SphereOfInfluence * child.SphereOfInfluence) {

                    best = child;
                    descended = true;

                    break;

                }

            }

        }

        return best;

    }

}
