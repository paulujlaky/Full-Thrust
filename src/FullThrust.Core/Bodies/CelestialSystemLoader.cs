using System.Text.Json;
using Brutal.Numerics;
using FullThrust.Core.Atmosphere;
using FullThrust.Core.Orbits;
using FullThrust.Core.Simulation;
using FullThrust.Core.Terrain;

namespace FullThrust.Core.Bodies;

public static class CelestialSystemLoader {

    public const double GravitationalConstant = 6.67430e-11;

    private static readonly JsonSerializerOptions Options = new() {

        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,

    };

    public static CelestialSystem LoadFile(string path, IOrbitPropagator propagator) => Load(File.ReadAllText(path), propagator);

    public static CelestialSystem Load(string json, IOrbitPropagator propagator) {

        var data = JsonSerializer.Deserialize<CelestialSystemData>(json, Options)
            ?? throw new InvalidDataException("Celestial system file is empty.");

        return Build(data, propagator);

    }

    public static CelestialSystem Build(CelestialSystemData data, IOrbitPropagator propagator) {

        var ordered = OrderParentsFirst(data.Bodies);
        var bodies = new CelestialBody[ordered.Count];

        for (var i = 0; i < ordered.Count; i++) {

            var definition = ordered[i];
            var parent = definition.Parent is null ? null : Array.Find(bodies, b => b is not null && b.Name == definition.Parent);

            bodies[i] = CreateBody(i, definition, parent);
            parent?.ChildList.Add(bodies[i]);

        }

        return new CelestialSystem(data.Name, bodies, propagator, Sun.FromData(data.Sun));

    }

    private static CelestialBody CreateBody(int index, CelestialBodyData definition, CelestialBody? parent) {

        Validate(definition, parent);

        var mu = definition.GravitationalParameter ?? GravitationalConstant * definition.Mass;
        var mass = definition.Mass > 0 ? definition.Mass : mu / GravitationalConstant;

        var color = definition.DebugColor.Length >= 3
            ? new float3((float)definition.DebugColor[0], (float)definition.DebugColor[1], (float)definition.DebugColor[2])
            : new float3(0.6f);

        if (parent is null) {

            return new CelestialBody {

                Index = index,
                Name = definition.Name,
                Mass = mass,
                GravitationalParameter = mu,
                Radius = definition.Radius,
                RotationPeriod = definition.RotationPeriod,
                AxialTilt = Radians(definition.AxialTiltDeg),
                InitialRotation = Radians(definition.InitialRotationDeg),
                Atmosphere = definition.Atmosphere,
                AtmosphereModel = CreateAtmosphere(definition, mu),
                Terrain = CreateTerrain(definition),
                TerrainData = definition.Terrain,
                DebugColor = color,

            };

        }

        var orbit = definition.Orbit!;

        var elements = new OrbitalElements {

            SemiMajorAxis = orbit.SemiMajorAxis,
            Eccentricity = orbit.Eccentricity,
            Inclination = Radians(orbit.InclinationDeg),
            LongitudeOfAscendingNode = Radians(orbit.LongitudeOfAscendingNodeDeg),
            ArgumentOfPeriapsis = Radians(orbit.ArgumentOfPeriapsisDeg),
            TrueAnomaly = OrbitalElements.TrueFromMeanAnomaly(Radians(orbit.MeanAnomalyAtEpochDeg), orbit.Eccentricity),

        };

        return new CelestialBody {

            Index = index,
            Name = definition.Name,
            Parent = parent,
            Mass = mass,
            GravitationalParameter = mu,
            Radius = definition.Radius,
            RotationPeriod = definition.RotationPeriod,
            AxialTilt = Radians(definition.AxialTiltDeg),
            InitialRotation = Radians(definition.InitialRotationDeg),
            Orbit = elements,
            OrbitEpoch = orbit.Epoch,
            OrbitGravitationalParameter = parent.GravitationalParameter + mu,
            EpochState = elements.ToState(parent.GravitationalParameter + mu),
            OrbitalPeriod = elements.Period(parent.GravitationalParameter + mu),
            SphereOfInfluence = SphereOfInfluence.Radius(orbit.SemiMajorAxis, mass, parent.Mass),
            Atmosphere = definition.Atmosphere,
            AtmosphereModel = CreateAtmosphere(definition, mu),
            Terrain = CreateTerrain(definition),
            TerrainData = definition.Terrain,
            DebugColor = color,

        };

    }

    private static LayeredAtmosphere? CreateAtmosphere(CelestialBodyData definition, double mu) {

        if (definition.Atmosphere is not { } atmosphere) {

            return null;

        }

        return new LayeredAtmosphere(atmosphere, mu / (definition.Radius * definition.Radius), definition.Radius);

    }

    private static ProceduralTerrain? CreateTerrain(CelestialBodyData definition) => definition.Terrain is { } terrain ? new ProceduralTerrain(terrain, definition.Radius) : null;

    private static void Validate(CelestialBodyData definition, CelestialBody? parent) {

        if (string.IsNullOrWhiteSpace(definition.Name)) {

            throw new InvalidDataException("Every celestial body needs a name.");

        }

        if (definition.Radius <= 0) {

            throw new InvalidDataException($"{definition.Name}: radius must be positive.");

        }

        if (definition.Mass <= 0 && definition.GravitationalParameter is not > 0) {

            throw new InvalidDataException($"{definition.Name}: mass or gravitationalParameter must be positive.");

        }

        if (parent is not null && definition.Orbit is null) {

            throw new InvalidDataException($"{definition.Name}: bodies with a parent need an orbit.");

        }

        if (definition.Orbit is { } orbit && (orbit.SemiMajorAxis <= 0 || orbit.Eccentricity is < 0 or >= 1)) {

            throw new InvalidDataException($"{definition.Name}: body orbits must be closed ellipses.");

        }

    }

    private static List<CelestialBodyData> OrderParentsFirst(List<CelestialBodyData> bodies) {

        var roots = bodies.FindAll(b => b.Parent is null);

        if (roots.Count != 1) {

            throw new InvalidDataException($"Expected exactly one root body, found {roots.Count}.");

        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<CelestialBodyData>(bodies.Count) { roots[0] };

        names.Add(roots[0].Name);

        var progressed = true;

        while (ordered.Count < bodies.Count && progressed) {

            progressed = false;

            foreach (var body in bodies) {

                if (body.Parent is null || names.Contains(body.Name) || !names.Contains(body.Parent)) {

                    continue;

                }

                ordered.Add(body);
                names.Add(body.Name);
                progressed = true;

            }

        }

        if (ordered.Count != bodies.Count) {

            throw new InvalidDataException("Celestial bodies reference unknown parents or contain duplicates.");

        }

        return ordered;

    }

    private static double Radians(double degrees) => degrees * Math.PI / 180.0;

}

public static class SphereOfInfluence {

    // Laplace sphere of influence.
    public static double Radius(double semiMajorAxis, double mass, double parentMass) => semiMajorAxis * Math.Pow(mass / parentMass, 0.4);

}
