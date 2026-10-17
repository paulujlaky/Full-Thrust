using Brutal.Numerics;
using FullThrust.Core.Ecs;
using FullThrust.Core.Parts;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Craft;

// Thrust follows mass flow and back-pressure, so Isp falls with ambient pressure without a lookup table.
public sealed class EngineSystem : ISystem {

    // Propellant must be at least this settled for a reliable start.
    public const double UllageRequired = 0.5;

    // Pressure-fed chambers can't hold combustion below this fraction of their design feed pressure.
    public const double MinFeedFraction = 0.3;

    public void Update(World world, in SimTime time) {

        foreach (var row in world.Query<FlightState>()) {

            row.C1.Thrust = 0;

        }

        var dt = time.DeltaTime;

        var flights = world.Store<FlightState>();
        var controls = world.Store<CraftControl>();
        var vessels = world.Store<Vessel>();
        var commands = world.Store<AttitudeCommand>();
        var masses = world.Store<CraftMass>();
        var forces = world.Store<CraftForces>();
        var transforms = world.Store<Transform>();

        foreach (var row in world.Query<EngineState, Part>()) {

            ref var engine = ref row.C1;
            ref readonly var part = ref row.C2;

            var craft = part.Craft;

            ref var flight = ref flights.Get(craft);
            ref readonly var control = ref controls.Get(craft);
            ref readonly var mass = ref masses.Get(craft);
            ref var command = ref commands.Get(craft);

            var physics = flight.Mode == FlightMode.Physics;
            var throttle = engine.Armed && vessels.Get(craft).Controllable ? control.Throttle : 0f;

            Step(world, ref engine, in part, ref flight, throttle, physics, dt);
            Steer(ref engine, in part, in command, in mass, dt);

            if (!physics || engine.Thrust <= 0) {

                continue;

            }

            var rotation = transforms.Get(craft).Rotation;
            var lever = part.LocalPosition - mass.CenterOfMass;
            var force = rotation * (engine.ThrustDirection * engine.Thrust);

            forces.Get(craft).AddAt(force, rotation * lever);
            flight.Thrust += engine.Thrust;

            command.AuthorityNext += GimbalAuthority(engine.Thrust, engine.Definition.GimbalRange, lever);

        }

    }

    public static void Step(World world, ref EngineState engine, in Part part, ref FlightState flight, float throttle, bool physics, double dt) {

        var definition = engine.Definition;
        var survey = PropellantFeed.Survey(world, part.Craft, part.FeedGroup, definition.Fuel, definition.Oxidizer);

        // Any non-zero throttle runs the engine at least at its minimum; zero is a commanded shutdown.
        var required = throttle > 0 ? definition.MinThrottle + (1 - definition.MinThrottle) * throttle : 0;
        var rising = throttle > 0 && !engine.CommandHigh;

        engine.CommandHigh = throttle > 0;

        var lit = engine.Status is EngineStatus.Starting or EngineStatus.Running;

        if (lit && required <= 0) {

            engine.Status = EngineStatus.Off;
            engine.Fault = EngineFault.None;
            lit = false;

        } else if (!lit && rising && physics) {

            lit = TryIgnite(ref engine, in survey, in flight);

        }

        var target = lit ? required : 0;
        var spool = target > engine.Pump ? definition.SpoolUpTime : definition.SpoolDownTime;

        // Spool times are to ~95% of target, i.e. three first-order time constants.
        engine.Pump += (target - engine.Pump) * (1 - Math.Exp(-3 * dt / spool));

        if (engine.Pump < 1e-4 && !lit) {

            engine.Pump = 0;

        }

        if (engine.Status == EngineStatus.Starting && engine.Pump >= 0.95 * required) {

            engine.Status = EngineStatus.Running;

        }

        var flow = engine.Pump * FeedFactor(ref engine, in survey);
        var massFlow = definition.MaxMassFlow * flow;

        var fuelNeeded = massFlow * dt * definition.FuelFraction;
        var oxidizerNeeded = massFlow * dt * definition.OxidizerFraction;

        var supply = 1.0;

        if (fuelNeeded > 0) {

            supply = Math.Min(supply, survey.Fuel / fuelNeeded);

        }

        if (oxidizerNeeded > 0) {

            supply = Math.Min(supply, survey.Oxidizer / oxidizerNeeded);

        }

        if (supply < 1 && massFlow > 0) {

            Flameout(ref engine, EngineFault.Depleted);

        }

        supply = Math.Max(0, supply);
        massFlow *= supply;

        PropellantFeed.Draw(world, part.Craft, part.FeedGroup, definition.Fuel, fuelNeeded * supply, survey.Fuel);

        if (definition.Oxidizer >= 0) {

            PropellantFeed.Draw(world, part.Craft, part.FeedGroup, definition.Oxidizer, oxidizerNeeded * supply, survey.Oxidizer);

        }

        engine.MassFlow = massFlow;
        engine.Thrust = definition.ThrustAt(massFlow, flight.Ambient.Pressure);
        engine.Isp = massFlow > 0 ? engine.Thrust / (massFlow * PhysicalConstants.StandardGravity) : 0;

    }

    private static bool TryIgnite(ref EngineState engine, in FeedSurvey survey, in FlightState flight) {

        var definition = engine.Definition;

        if (engine.IgnitionsRemaining == 0) {

            return Fail(ref engine, EngineFault.NoIgnitions);

        }

        if (survey.Fuel <= 0 || (definition.Oxidizer >= 0 && survey.Oxidizer <= 0)) {

            return Fail(ref engine, EngineFault.NoPropellant);

        }

        // From here an igniter is spent whether or not the start succeeds.
        if (engine.IgnitionsRemaining > 0) {

            engine.IgnitionsRemaining--;

        }

        if (definition.RequiresUllage && flight.UllageSettle < UllageRequired) {

            return Fail(ref engine, EngineFault.Unsettled);

        }

        if (definition.Feed == EngineFeed.Turbopump && survey.InletPressure < definition.MinInletPressure) {

            return Fail(ref engine, EngineFault.LowInletPressure);

        }

        engine.Status = EngineStatus.Starting;
        engine.Fault = EngineFault.None;

        return true;

    }

    private static double FeedFactor(ref EngineState engine, in FeedSurvey survey) {

        var definition = engine.Definition;

        if (engine.Pump <= 0) {

            return 0;

        }

        if (definition.Feed == EngineFeed.Turbopump) {

            // Below the required inlet pressure the pump cavitates and the engine is lost.
            if (survey.InletPressure < definition.MinInletPressure && engine.Status != EngineStatus.Off) {

                Flameout(ref engine, EngineFault.Cavitation);

            }

            return 1;

        }

        var fraction = definition.FeedPressure > 0 ? Math.Clamp(survey.InletPressure / definition.FeedPressure, 0, 1) : 1;

        if (fraction < MinFeedFraction && engine.Status is EngineStatus.Starting or EngineStatus.Running) {

            Flameout(ref engine, EngineFault.LowFeedPressure);

        }

        return fraction;

    }

    private static void Flameout(ref EngineState engine, EngineFault fault) {

        if (engine.Status is EngineStatus.Starting or EngineStatus.Running) {

            engine.Status = EngineStatus.Flameout;
            engine.Fault = fault;

        }

    }

    private static bool Fail(ref EngineState engine, EngineFault fault) {

        engine.Status = EngineStatus.IgnitionFailed;
        engine.Fault = fault;

        return false;

    }

    // Gimbal deflects thrust along command x lever, which turns into torque along the commanded axis.
    public static void Steer(ref EngineState engine, in Part part, in AttitudeCommand command, in CraftMass mass, double dt) {

        var definition = engine.Definition;
        var axis = part.LocalRotation * double3.UnitY;
        var target = double3.Zero;

        if (definition.GimbalRange > 0) {

            var lever = double3.NormalizeOrZero(part.LocalPosition - mass.CenterOfMass);
            var deflection = double3.Cross(command.Rotation, lever);

            deflection -= axis * double3.Dot(deflection, axis);

            var length = deflection.Length();

            if (length > 1) {

                deflection /= length;

            }

            target = deflection * definition.GimbalRange;

        }

        var step = target - engine.Gimbal;
        var maxStep = definition.GimbalRate * dt;
        var distance = step.Length();

        engine.Gimbal = distance > maxStep ? engine.Gimbal + step * (maxStep / distance) : target;
        engine.ThrustDirection = double3.Normalize(axis + engine.Gimbal);

    }

    private static double3 GimbalAuthority(double thrust, double range, double3 lever) {

        var side = thrust * Math.Sin(range);
        var radial = Math.Sqrt(lever.X * lever.X + lever.Z * lever.Z);

        return new double3(side * Math.Abs(lever.Y), side * radial, side * Math.Abs(lever.Y));

    }

}
