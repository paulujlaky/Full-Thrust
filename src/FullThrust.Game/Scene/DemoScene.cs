using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Craft;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Rendering;
using FullThrust.Core.Space;
using FullThrust.Core.Terrain;

namespace FullThrust.Game.Scene;

public enum Scenario {

    Launchpad,
    LowOrbit,
    LunarTransfer,
    Reentry,

}

public sealed class DemoScene {

    public const double ParkingAltitude = 200_000.0;

    // A capsule returning from orbit, just above where the plasma starts to glow.
    public const double EntryAltitude = 90_000.0;
    public const double EntrySpeed = 7_600.0;
    public const double EntryAngleDeg = -1.5;

    // A Cape Canaveral-like latitude; longitude is arbitrary on a featureless planet.
    public const double LaunchLatitudeDeg = 28.5;
    public const double LaunchLongitudeDeg = 0.0;

    public const string LaunchPadModel = "scene/launchpad.glb";

    // Lifts the pad's surface just clear of the flattened terrain so the two never z-fight; craft clear it anyway.
    public const double LaunchPadLift = 0.03;

    private Entity[] _bodyEntities = [];

    public DemoScene(CelestialSystem system, RocketCatalog rockets, FlightFocus focus, ModelCatalog models) {

        System = system;
        Models = models;
        Rockets = rockets;
        Focus = focus;
        Moon = system.Find("Moon") ?? throw new InvalidDataException("The demo scene expects a body named Moon.");
        Rocket = rockets.Rockets[0];

    }

    public CelestialSystem System { get; }

    public ModelCatalog Models { get; }

    // Overrides the scenario's starting attitude in orbit; reentry otherwise starts tail first, so a capsule leads with its shield.
    public Attitude? Attitude { get; set; }

    public Entity LaunchPad { get; private set; }

    public RocketCatalog Rockets { get; }

    public FlightFocus Focus { get; }

    public CelestialBody Moon { get; }

    public Entity Craft => Focus.Craft;

    public Scenario Scenario { get; private set; }

    public RocketPreset Rocket { get; private set; }

    public Entity BodyEntity(int index) => _bodyEntities[index];

    public void Populate(World world, Scenario scenario, RocketPreset rocket, double universalTime) {

        _bodyEntities = new Entity[System.Count];

        foreach (var body in System.Bodies) {

            var entity = world.Create();
            var radius = (float)body.Radius;

            world.Add(entity, new Name(body.Name));
            world.Add(entity, new CelestialBodyRef(body.Index));
            world.Add(entity, new Transform(System.Position(body.Index), body.RotationAt(universalTime)));

            // Bodies with terrain are drawn by the terrain renderer. Alpha below 1 asks the debug shader for a checker so rotation is visible.
            if (body.Terrain is null) {

                world.Add(entity, new DebugShape { Kind = ShapeKind.Sphere, Scale = new float3(radius), Color = new float4(body.DebugColor, 0.8f) });

            }

            _bodyEntities[body.Index] = entity;

        }

        SpawnLaunchPad(world);
        Spawn(world, scenario, rocket, universalTime);

    }

    // The pad sits on the planet's first terrain site, flush with the flattened ground.
    private void SpawnLaunchPad(World world) {

        var planet = Moon.Parent!;

        if (planet.Terrain is not { Sites.Count: > 0 } terrain) {

            return;

        }

        var site = terrain.Sites[0];
        var model = Models.Register(LaunchPadModel);

        LaunchPad = world.Create();

        world.Add(LaunchPad, new Name(site.Name));
        world.Add(LaunchPad, new SurfaceAnchor { Body = planet.Index, Position = site.Direction * (planet.Radius + site.Height + LaunchPadLift), Rotation = SpaceMath.FromTo(double3.UnitY, site.Direction) });
        world.Add(LaunchPad, new Transform(System.Position(planet.Index), doubleQuat.Identity));
        world.Add(LaunchPad, new ModelInstance { Model = model.Index });

        foreach (var grove in planet.TerrainData!.Sites[0].Groves) {

            PlantGroves(world, planet, terrain, site, grove);

        }

    }

    private void PlantGroves(World world, CelestialBody body, ITerrain terrain, in TerrainSite site, GroveData grove) {

        if (grove.InnerRadius < 0 || grove.OuterRadius <= grove.InnerRadius || grove.FootprintRadius <= 0) {

            throw new InvalidDataException($"Invalid grove footprint at {site.Name}.");

        }

        var model = Models.Register(grove.Model);
        var random = new Random(grove.Seed);
        var habitats = new GradientNoise(body.TerrainData!.Seed);
        var siteRotation = SpaceMath.FromTo(double3.UnitY, site.Direction);
        var planted = 0;

        for (var i = 0; i < grove.Count * 3 && planted < grove.Count; i++) {

            var angle = random.NextDouble() * Math.Tau;
            var distance = Math.Sqrt(double.Lerp(grove.InnerRadius * grove.InnerRadius, grove.OuterRadius * grove.OuterRadius, random.NextDouble()));
            var localX = Math.Cos(angle) * distance;
            var localZ = Math.Sin(angle) * distance;
            var habitat = habitats.Noise(localX / 190, 7.3, localZ / 190) + 0.45 * habitats.Noise(localX / 72, 1.9, localZ / 72);

            if (habitat < 0.06 && random.NextDouble() > 0.13) {

                continue;

            }

            var offset = siteRotation * new double3(localX, 0, localZ);
            var direction = double3.Normalize(site.Direction * body.Radius + offset);
            var center = direction * body.Radius;
            var height = terrain.Height(direction);
            var rotation = SpaceMath.FromTo(double3.UnitY, direction) * doubleQuat.CreateFromAxisAngle(double3.UnitY, random.NextDouble() * Math.Tau);
            var x = rotation * double3.UnitX;
            var z = rotation * double3.UnitZ;
            var radius = grove.FootprintRadius;
            var east = terrain.Height(double3.Normalize(center + x * radius));
            var west = terrain.Height(double3.Normalize(center - x * radius));
            var north = terrain.Height(double3.Normalize(center + z * radius));
            var south = terrain.Height(double3.Normalize(center - z * radius));

            // A shared stand fits a local tangent plane; leave broken slopes and the shore clear.
            var lowest = Math.Min(height, Math.Min(Math.Min(east, west), Math.Min(north, south)));
            var curvature = Math.Max(Math.Abs((east + west) * 0.5 - height), Math.Abs((north + south) * 0.5 - height));

            if ((terrain.HasOcean && lowest < 0.7) || curvature > 0.6) {

                continue;

            }

            var normal = double3.Normalize(direction - (x * (east - west) + z * (north - south)) / (2 * radius));
            var stand = world.Create();
            world.Add(stand, new SurfaceAnchor {

                Body = body.Index,
                Position = center + direction * height,
                Rotation = SpaceMath.FromTo(direction, normal) * rotation,

            });
            world.Add(stand, new Transform(System.Position(body.Index), doubleQuat.Identity));
            world.Add(stand, new ModelInstance {

                Model = model.Index,
                DetailRange = grove.FootprintRadius * 1100,

            });
            planted++;

        }

    }

    private CraftPlacement LaunchPlacement(CelestialBody planet, RocketPreset rocket, double universalTime) {

        if (planet.Terrain is { Sites.Count: > 0 } terrain) {

            var site = terrain.Sites[0];

            return CraftPlacement.OnSurface(System, planet, site.Latitude, site.Longitude, rocket, universalTime);

        }

        return CraftPlacement.OnSurface(System, planet, LaunchLatitudeDeg * Math.PI / 180, LaunchLongitudeDeg * Math.PI / 180, rocket, universalTime);

    }

    // Callers remove the previous craft (and their physics bodies) first.
    public Entity Spawn(World world, Scenario scenario, RocketPreset rocket, double universalTime) {

        Scenario = scenario;
        Rocket = rocket;

        var planet = Moon.Parent!;
        var parking = planet.Radius + ParkingAltitude;

        var placement = scenario switch {

            Scenario.Launchpad => LaunchPlacement(planet, rocket, universalTime),
            Scenario.LunarTransfer => CraftPlacement.InOrbit(System, planet, OrbitScenarios.LunarTransfer(System, Moon, parking, universalTime), universalTime),
            Scenario.LowOrbit => CraftPlacement.InOrbit(System, planet, OrbitScenarios.Circular(planet.GravitationalParameter, parking), universalTime, Attitude ?? Core.Craft.Attitude.Prograde),
            Scenario.Reentry => CraftPlacement.InOrbit(System, planet, OrbitScenarios.Entry(planet.GravitationalParameter, planet.Radius + EntryAltitude, EntrySpeed, EntryAngleDeg * Math.PI / 180), universalTime, Attitude ?? Core.Craft.Attitude.Retrograde),
            _ => CraftPlacement.InOrbit(System, planet, OrbitScenarios.Circular(planet.GravitationalParameter, parking), universalTime),

        };

        Focus.Craft = placement.Spawn(world, rocket);

        world.Get<CraftControl>(Focus.Craft).Rcs = scenario != Scenario.Launchpad;

        return Focus.Craft;

    }

}
