using Brutal.Numerics;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Parts;
using FullThrust.Core.Physics;
using FullThrust.Core.Rendering;
using FullThrust.Core.Space;

namespace FullThrust.Core.Craft;

public static class CraftAssembly {

    public const int MaxPartsPerCraft = 256;

    public static Entity Spawn(World world, RocketPreset preset, in Transform pose, in Velocity velocity, in OrbitState orbit) {

        if (preset.Parts.Length > MaxPartsPerCraft) {

            throw new InvalidOperationException($"{preset.Id} has more than {MaxPartsPerCraft} parts.");

        }

        var craft = world.Create();

        world.Add(craft, new Name(preset.Name));
        world.Add(craft, pose);
        world.Add(craft, velocity);
        world.Add(craft, orbit);
        world.Add(craft, new CraftControl());
        world.Add(craft, new AttitudeCommand());
        world.Add(craft, new CraftForces());
        world.Add(craft, new CraftStaging { StageCount = preset.StageCount });
        world.Add(craft, new FlightState { Mode = FlightMode.Rails, Body = orbit.Body, UllageSettle = 1 });

        Span<Entity> entities = stackalloc Entity[preset.Parts.Length];

        for (var i = 0; i < preset.Parts.Length; i++) {

            ref readonly var spec = ref preset.Parts[i];

            var definition = spec.Definition;
            var part = world.Create();

            entities[i] = part;

            world.Add(part, new Part {

                Craft = craft,
                Parent = spec.Parent >= 0 ? entities[spec.Parent] : Entity.Null,
                Definition = definition,

                LocalPosition = spec.Position,
                LocalRotation = spec.Rotation,
                Radial = spec.Radial,

                Stage = spec.Stage,

            });

            world.Add(part, new Transform(pose.Position + pose.Rotation * spec.Position, pose.Rotation * spec.Rotation));

            world.Add(part, new DebugShape {

                Kind = definition.Shape,
                Scale = new float3((float)(definition.Radius * 2), (float)definition.Length, (float)(definition.Radius * 2)),
                Color = definition.Color,

            });

            definition.Attach(world, part);

        }

        world.Add(craft, new Vessel { Root = entities[0], ShapeDirty = true });
        world.Add(craft, ComputeMass(world, craft));

        RebuildTopology(world, craft);

        return craft;

    }

    public static void Destroy(World world, Entity craft) {

        Span<Entity> parts = stackalloc Entity[MaxPartsPerCraft];

        var count = 0;

        foreach (var row in world.Query<Part>()) {

            if (row.C1.Craft == craft && count < parts.Length) {

                parts[count++] = row.Entity;

            }

        }

        for (var i = 0; i < count; i++) {

            world.Destroy(parts[i]);

        }

        world.Destroy(craft);

    }

    // Feed groups, exposed drag areas and the vessel summary all depend on which parts are still attached.
    public static void RebuildTopology(World world, Entity craft) {

        var parts = world.Store<Part>();
        var decouplers = world.Store<DecouplerState>();
        var pods = world.Store<CommandPod>();

        ref var vessel = ref world.Get<Vessel>(craft);

        vessel.PartCount = 0;
        vessel.Controllable = false;
        vessel.Bottom = 0;
        vessel.ShapeDirty = true;

        foreach (var row in world.Query<Part>()) {

            ref var part = ref row.C1;

            if (part.Craft != craft) {

                continue;

            }

            var definition = part.Definition;

            vessel.PartCount++;
            vessel.Controllable |= pods.Has(row.Entity);
            vessel.Bottom = Math.Min(vessel.Bottom, part.LocalPosition.Y - definition.Length / 2);

            if (part.Parent.IsNull || !parts.Has(part.Parent) || parts.Get(part.Parent).Craft != craft) {

                vessel.Root = row.Entity;

            }

            part.FeedGroup = FeedGroupOf(row.Entity, craft, parts, decouplers);
            part.Aero = ExposedAreas(world, row.Entity, in part, craft);

        }

    }

    public static int CollectShape(World world, Entity craft, Span<ShapeElement> shape) {

        var count = 0;

        foreach (var row in world.Query<Part>()) {

            ref readonly var part = ref row.C1;

            if (part.Craft != craft || count >= shape.Length) {

                continue;

            }

            var definition = part.Definition;

            shape[count++] = new ShapeElement(ShapePrimitive.Cylinder, part.LocalPosition, part.LocalRotation, new double3(definition.Radius, definition.Length / 2, definition.Radius));

        }

        return count;

    }

    public static CraftMass ComputeMass(World world, Entity craft) {

        var tanks = world.Store<TankState>();

        var mass = 0.0;
        var moment = double3.Zero;

        foreach (var row in world.Query<Part>()) {

            ref readonly var part = ref row.C1;

            if (part.Craft != craft) {

                continue;

            }

            var dry = part.Definition.DryMass;

            mass += dry;
            moment += part.LocalPosition * dry;

            ref readonly var tank = ref tanks.TryGet(row.Entity, out var hasTank);

            if (hasTank) {

                var propellant = tank.PropellantMass;

                mass += propellant + tank.PressurantMass;
                moment += PropellantCenter(in part, in tank) * propellant + part.LocalPosition * tank.PressurantMass;

            }

        }

        var result = new CraftMass { Mass = mass, CenterOfMass = mass > 0 ? moment / mass : double3.Zero };
        var com = result.CenterOfMass;

        foreach (var row in world.Query<Part>()) {

            ref readonly var part = ref row.C1;

            if (part.Craft != craft) {

                continue;

            }

            var definition = part.Definition;

            result.Inertia.Add(InertiaTensor.SolidCylinder(definition.DryMass, definition.Radius, definition.Length));
            result.Inertia.AddOffset(definition.DryMass, part.LocalPosition - com);

            ref readonly var tank = ref tanks.TryGet(row.Entity, out var hasTank);

            if (hasTank) {

                var propellant = tank.PropellantMass;

                result.Inertia.Add(InertiaTensor.SolidCylinder(propellant, definition.Radius, definition.Length * tank.FillFraction));
                result.Inertia.AddOffset(propellant, PropellantCenter(in part, in tank) - com);
                result.Inertia.AddOffset(tank.PressurantMass, part.LocalPosition - com);

            }

        }

        return result;

    }

    // Settled propellant pools at the aft end of the tank, so the centre of mass climbs as it drains.
    public static double3 PropellantCenter(in Part part, in TankState tank) {

        var length = part.Definition.Length;
        var aft = part.LocalRotation * new double3(0, -1, 0);

        return part.LocalPosition + aft * (length / 2 - length * tank.FillFraction / 2);

    }

    private static int FeedGroupOf(Entity entity, Entity craft, ComponentStore<Part> parts, ComponentStore<DecouplerState> decouplers) {

        var current = entity;

        for (var depth = 0; depth < MaxPartsPerCraft; depth++) {

            if (decouplers.Has(current)) {

                return current.Index;

            }

            var parent = parts.Get(current).Parent;

            if (parent.IsNull || !parts.Has(parent) || parts.Get(parent).Craft != craft) {

                return current.Index;

            }

            current = parent;

        }

        return current.Index;

    }

    private static PartAero ExposedAreas(World world, Entity entity, in Part part, Entity craft) {

        var definition = part.Definition;
        var area = definition.FrontalArea;

        var aero = new PartAero { TopArea = area, BottomArea = area, SideArea = 2 * definition.Radius * definition.Length };

        if (part.Radial) {

            return aero;

        }

        var parts = world.Store<Part>();

        if (!part.Parent.IsNull && parts.Has(part.Parent)) {

            ref readonly var parent = ref parts.Get(part.Parent);

            if (parent.Craft == craft && !parent.Radial) {

                var covered = Math.Min(definition.Radius, parent.Definition.Radius);

                aero.TopArea -= Math.PI * covered * covered;

            }

        }

        foreach (var row in world.Query<Part>()) {

            ref readonly var child = ref row.C1;

            if (child.Parent != entity || child.Craft != craft || child.Radial) {

                continue;

            }

            var covered = Math.Min(definition.Radius, child.Definition.Radius);

            aero.BottomArea -= Math.PI * covered * covered;

        }

        aero.TopArea = Math.Max(0, aero.TopArea);
        aero.BottomArea = Math.Max(0, aero.BottomArea);

        return aero;

    }

}
