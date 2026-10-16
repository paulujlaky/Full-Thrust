using Brutal.Numerics;
using FullThrust.Core.Craft;
using FullThrust.Core.Ecs;
using FullThrust.Core.Rendering;
using FullThrust.Core.Space;

namespace FullThrust.Game.Scene;

// Placeholder bodies until Sprint 2 introduces data-driven celestial bodies.
public sealed class DemoScene {

    public const double PlanetRadius = 6_371_000.0;
    public const double MoonRadius = 1_737_000.0;
    public const double MoonDistance = 384_400_000.0;

    public const double LaunchAltitude = 100_000.0;

    public Entity Planet { get; private set; }

    public Entity Moon { get; private set; }

    public Entity Craft { get; private set; }

    public void Populate(World world) {

        Planet = world.Create();
        world.Add(Planet, new Name("Planet"));
        world.Add(Planet, new Transform(double3.Zero, doubleQuat.Identity));
        world.Add(Planet, new DebugShape { Kind = ShapeKind.Sphere, Scale = new float3((float)PlanetRadius), Color = new float4(0.18f, 0.35f, 0.65f, 1) });

        Moon = world.Create();
        world.Add(Moon, new Name("Moon"));
        world.Add(Moon, new Transform(new double3(MoonDistance, 0, 0), doubleQuat.Identity));
        world.Add(Moon, new DebugShape { Kind = ShapeKind.Sphere, Scale = new float3((float)MoonRadius), Color = new float4(0.55f, 0.55f, 0.52f, 1) });

        var craftPosition = new double3(0, PlanetRadius + LaunchAltitude, 0);

        Craft = world.Create();
        world.Add(Craft, new Name("Craft"));
        world.Add(Craft, new Transform(craftPosition, doubleQuat.Identity));
        world.Add(Craft, new Velocity());
        world.Add(Craft, new CraftControl());
        world.Add(Craft, new ControllableCraft { Mass = 5_000, MaxThrust = 150_000, MaxAngularAcceleration = 1.5, AngularDamping = 1.0 });
        world.Add(Craft, new DebugShape { Kind = ShapeKind.Box, Scale = new float3(1.25f, 6f, 1.25f), Color = new float4(0.85f, 0.85f, 0.88f, 1) });

        AddMarker(world, craftPosition + new double3(12, 0, 0), new float4(0.9f, 0.2f, 0.2f, 1));
        AddMarker(world, craftPosition + new double3(0, 0, 12), new float4(0.2f, 0.9f, 0.2f, 1));
        AddMarker(world, craftPosition + new double3(-12, 0, -12), new float4(0.9f, 0.8f, 0.2f, 1));

    }

    private static void AddMarker(World world, double3 position, float4 color) {

        var marker = world.Create();

        world.Add(marker, new Name("Marker"));
        world.Add(marker, new Transform(position, doubleQuat.Identity));
        world.Add(marker, new DebugShape { Kind = ShapeKind.Box, Scale = new float3(1.0f), Color = color });

    }

}
