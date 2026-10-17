using FullThrust.Core.Bodies;
using FullThrust.Core.Ecs;
using FullThrust.Core.Orbits;
using FullThrust.Core.Physics;
using FullThrust.Core.Space;

namespace FullThrust.Core.Craft;

// Order matters: craft must exist and have mass before actuators add forces, and forces apply once at the end.
public sealed class FlightPipeline {

    public FlightPipeline(CelestialSystem system, IRigidBodyWorld bodies, PhysicsFrame frame, FlightFocus focus, OrbitEvents events) {

        Staging = new StagingSystem();
        Environment = new EnvironmentSystem(system);
        Modes = new FlightModeSystem(system, bodies, frame, focus);
        Mass = new CraftMassSystem(bodies);
        Attitude = new AttitudeControlSystem();
        Engines = new EngineSystem();
        Rcs = new RcsSystem();
        Wheels = new ReactionWheelSystem();
        Tanks = new TankPressureSystem();
        Aerodynamics = new AerodynamicsSystem();
        Ground = new GroundContactSystem(system, bodies, frame, focus);
        RigidBodies = new RigidBodySystem(system, bodies, frame, focus, events);
        Kinematics = new KinematicsSystem();
        PartTransforms = new PartTransformSystem();

    }

    public StagingSystem Staging { get; }

    public EnvironmentSystem Environment { get; }

    public FlightModeSystem Modes { get; }

    public CraftMassSystem Mass { get; }

    public AttitudeControlSystem Attitude { get; }

    public EngineSystem Engines { get; }

    public RcsSystem Rcs { get; }

    public ReactionWheelSystem Wheels { get; }

    public TankPressureSystem Tanks { get; }

    public AerodynamicsSystem Aerodynamics { get; }

    public GroundContactSystem Ground { get; }

    public RigidBodySystem RigidBodies { get; }

    public KinematicsSystem Kinematics { get; }

    // Runs in LateUpdate after orbit propagation so rails craft draw their parts where the conic put them.
    public PartTransformSystem PartTransforms { get; }

    public SystemScheduler AddFixedSystems(SystemScheduler scheduler) {

        return scheduler
            .Add(SystemPhase.FixedUpdate, Staging)
            .Add(SystemPhase.FixedUpdate, Environment)
            .Add(SystemPhase.FixedUpdate, Modes)
            .Add(SystemPhase.FixedUpdate, Mass)
            .Add(SystemPhase.FixedUpdate, Attitude)
            .Add(SystemPhase.FixedUpdate, Engines)
            .Add(SystemPhase.FixedUpdate, Rcs)
            .Add(SystemPhase.FixedUpdate, Wheels)
            .Add(SystemPhase.FixedUpdate, Tanks)
            .Add(SystemPhase.FixedUpdate, Aerodynamics)
            .Add(SystemPhase.FixedUpdate, Ground)
            .Add(SystemPhase.FixedUpdate, RigidBodies)
            .Add(SystemPhase.FixedUpdate, Kinematics);

    }

}
