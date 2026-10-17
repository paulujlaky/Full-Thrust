using Brutal.Numerics;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Parts;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Craft;

public readonly struct StageEvent {

    public readonly Entity Craft;

    public readonly int Stage;

    public readonly int EnginesArmed;
    public readonly int Separations;

    public readonly double Time;

    public StageEvent(Entity craft, int stage, int enginesArmed, int separations, double time) {

        Craft = craft;
        Stage = stage;
        EnginesArmed = enginesArmed;
        Separations = separations;
        Time = time;

    }

}

// Spent stages become new craft carrying the parent's pose and velocity, so separation needs no special physics.
public sealed class StagingSystem : ISystem {

    private const int MaxSeparationsPerStage = 32;

    public const double SeparationWindow = 5.0;

    private readonly Entity[] _decouplers = new Entity[MaxSeparationsPerStage];
    private readonly Entity[] _staging = new Entity[16];

    public long StageCount { get; private set; }

    public StageEvent LastEvent { get; private set; }

    public void Update(World world, in SimTime time) {

        var requests = 0;

        foreach (var row in world.Query<CraftStaging, Vessel>()) {

            ref var vessel = ref row.C2;

            vessel.SeparationTimer = Math.Max(0, vessel.SeparationTimer - time.DeltaTime);

            if (row.C1.Requested && requests < _staging.Length) {

                _staging[requests++] = row.Entity;

            }

            row.C1.Requested = false;

        }

        for (var i = 0; i < requests; i++) {

            Stage(world, _staging[i], time);

        }

    }

    public bool Stage(World world, Entity craft, in SimTime time) {

        ref var staging = ref world.Get<CraftStaging>(craft);

        if (staging.NextStage >= staging.StageCount) {

            return false;

        }

        var stage = staging.NextStage++;
        var armed = 0;
        var separations = 0;

        foreach (var row in world.Query<EngineState, Part>()) {

            if (row.C2.Craft != craft || row.C2.Stage != stage) {

                continue;

            }

            row.C1.Armed = true;
            row.C1.CommandHigh = false;
            armed++;

        }

        foreach (var row in world.Query<DecouplerState, Part>()) {

            if (row.C2.Craft != craft || row.C2.Stage != stage || row.C1.Fired || separations >= _decouplers.Length) {

                continue;

            }

            row.C1.Fired = true;
            _decouplers[separations++] = row.Entity;

        }

        for (var i = 0; i < separations; i++) {

            Separate(world, craft, _decouplers[i], time.DeltaTime);

        }

        StageCount++;
        LastEvent = new StageEvent(craft, stage, armed, separations, time.UniversalTime);

        return true;

    }

    private static void Separate(World world, Entity craft, Entity decoupler, double deltaTime) {

        var parts = world.Store<Part>();

        ref var decouplerPart = ref parts.Get(decoupler);

        if (decouplerPart.Craft != craft || decouplerPart.Parent.IsNull) {

            return;

        }

        var debris = world.Create();

        world.Add(debris, new Name("Debris"));
        world.Add(debris, world.Get<Transform>(craft));
        world.Add(debris, world.Get<Velocity>(craft));
        world.Add(debris, world.Get<OrbitState>(craft));
        world.Add(debris, new CraftControl());
        world.Add(debris, new AttitudeCommand());
        world.Add(debris, new CraftForces());
        world.Add(debris, new CraftStaging());
        world.Add(debris, world.Get<FlightState>(craft));
        world.Add(debris, new Vessel { Root = decoupler, SeparationTimer = SeparationWindow });

        foreach (var row in world.Query<Part>()) {

            if (row.C1.Craft == craft && IsInSubtree(parts, row.Entity, decoupler)) {

                row.C1.Craft = debris;

            }

        }

        // The decoupler stays on the spent stage; its old parent link now crosses craft.
        var anchor = parts.Get(decoupler).Parent;

        ref var parentVessel = ref world.Get<Vessel>(craft);

        parentVessel.SeparationTimer = SeparationWindow;

        CraftAssembly.RebuildTopology(world, craft);
        CraftAssembly.RebuildTopology(world, debris);

        world.Get<CraftMass>(craft) = CraftAssembly.ComputeMass(world, craft);
        world.Add(debris, CraftAssembly.ComputeMass(world, debris));

        ApplyEjection(world, craft, debris, decoupler, anchor, deltaTime);

    }

    private static void ApplyEjection(World world, Entity craft, Entity debris, Entity decoupler, Entity anchor, double deltaTime) {

        ref readonly var part = ref world.Get<Part>(decoupler);

        var impulse = world.Get<DecouplerState>(decoupler).Definition.EjectionImpulse;

        if (impulse <= 0) {

            return;

        }

        var rotation = world.Get<Transform>(craft).Rotation;

        // Push the stages apart along the axis from the spent stage towards the part it was holding.
        var axis = world.Get<Part>(anchor).LocalPosition - part.LocalPosition;
        var direction = rotation * double3.NormalizeOrZero(axis);

        // Delivered as a one-step force so it reaches bodies that already live in the physics engine.
        var force = direction * (impulse / deltaTime);

        world.Get<CraftForces>(craft).Force += force;
        world.Get<CraftForces>(debris).Force -= force;

        ref var upperFlight = ref world.Get<FlightState>(craft);

        // The kick settles the upper stage's propellant just as an ullage motor would.
        upperFlight.UllageSettle = 1;

    }

    private static bool IsInSubtree(ComponentStore<Part> parts, Entity entity, Entity root) {

        var current = entity;

        for (var depth = 0; depth < CraftAssembly.MaxPartsPerCraft && !current.IsNull; depth++) {

            if (current == root) {

                return true;

            }

            current = parts.Get(current).Parent;

        }

        return false;

    }

}
