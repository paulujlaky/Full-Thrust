using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Craft;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Rendering;
using FullThrust.Core.Space;

namespace FullThrust.Game.Scene;

public enum Scenario {

    Launchpad,
    LowOrbit,
    LunarTransfer,

}

public sealed class DemoScene {

    public const double ParkingAltitude = 200_000.0;

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
            _ => CraftPlacement.InOrbit(System, planet, OrbitScenarios.Circular(planet.GravitationalParameter, parking), universalTime),

        };

        Focus.Craft = placement.Spawn(world, rocket);

        world.Get<CraftControl>(Focus.Craft).Rcs = scenario != Scenario.Launchpad;

        return Focus.Craft;

    }

}
