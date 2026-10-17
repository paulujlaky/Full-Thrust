using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Craft;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Rendering;
using FullThrust.Core.Space;

namespace FullThrust.Game.Scene;

public enum Scenario {

    LowOrbit,
    LunarTransfer,

}

public sealed class DemoScene {

    public const double ParkingAltitude = 200_000.0;

    private Entity[] _bodyEntities = [];

    public DemoScene(CelestialSystem system) {

        System = system;
        Moon = system.Find("Moon") ?? throw new InvalidDataException("The demo scene expects a body named Moon.");

    }

    public CelestialSystem System { get; }

    public CelestialBody Moon { get; }

    public Entity Craft { get; private set; }

    public Scenario Scenario { get; private set; }

    public Entity BodyEntity(int index) => _bodyEntities[index];

    public void Populate(World world, Scenario scenario, double universalTime) {

        _bodyEntities = new Entity[System.Count];

        foreach (var body in System.Bodies) {

            var entity = world.Create();
            var radius = (float)body.Radius;

            world.Add(entity, new Name(body.Name));
            world.Add(entity, new CelestialBodyRef(body.Index));
            world.Add(entity, new Transform(System.Position(body.Index), body.RotationAt(universalTime)));

            // Alpha below 1 asks the debug shader for a checker so rotation is visible.
            world.Add(entity, new DebugShape { Kind = ShapeKind.Sphere, Scale = new float3(radius), Color = new float4(body.DebugColor, 0.8f) });

            _bodyEntities[body.Index] = entity;

        }

        Craft = world.Create();
        world.Add(Craft, new Name("Craft"));
        world.Add(Craft, new Transform(double3.Zero, doubleQuat.Identity));
        world.Add(Craft, new Velocity());
        world.Add(Craft, new ThrustAcceleration());
        world.Add(Craft, new CraftControl());
        world.Add(Craft, new ControllableCraft { Mass = 5_000, MaxThrust = 150_000, MaxAngularAcceleration = 1.5, AngularDamping = 1.0 });
        world.Add(Craft, new DebugShape { Kind = ShapeKind.Box, Scale = new float3(1.25f, 6f, 1.25f), Color = new float4(0.85f, 0.85f, 0.88f, 1) });
        world.Add(Craft, OrbitState.Create(0, default, universalTime));

        Reset(world, scenario, universalTime);

    }

    public void Reset(World world, Scenario scenario, double universalTime) {

        Scenario = scenario;

        var planet = Moon.Parent!;
        var parkingRadius = planet.Radius + ParkingAltitude;

        var state = scenario switch {

            Scenario.LunarTransfer => OrbitScenarios.LunarTransfer(System, Moon, parkingRadius, universalTime),
            _ => OrbitScenarios.Circular(planet.GravitationalParameter, parkingRadius),

        };

        ref var orbit = ref world.Get<OrbitState>(Craft);

        orbit.Rebase(planet.Index, state, universalTime);

        ref var transform = ref world.Get<Transform>(Craft);

        // Point the nose prograde so throttle raises the orbit.
        transform.Rotation = OrbitCameraRig.LookRotation(double3.Normalize(state.Position), double3.Normalize(state.Velocity));
        transform.Position = System.StateAt(planet.Index, universalTime).Position + state.Position;

        world.Get<Velocity>(Craft) = default;
        world.Get<CraftControl>(Craft) = default;

    }

}
