using Brutal.Numerics;
using FullThrust.Core.Ecs;
using FullThrust.Core.Parts;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Craft;

// Nozzle levels blend translation and torque alignment, so any block layout works without a mixing table.
public sealed class RcsSystem : ISystem {

    public const double Deadband = 0.05;

    public void Update(World world, in SimTime time) {

        var dt = time.DeltaTime;

        var flights = world.Store<FlightState>();
        var controls = world.Store<CraftControl>();
        var commands = world.Store<AttitudeCommand>();
        var masses = world.Store<CraftMass>();
        var forces = world.Store<CraftForces>();
        var transforms = world.Store<Transform>();

        foreach (var row in world.Query<RcsState, Part>()) {

            ref var rcs = ref row.C1;
            ref readonly var part = ref row.C2;

            var craft = part.Craft;

            rcs.Thrust = 0;
            rcs.MassFlow = 0;
            rcs.Firing = 0;

            ref var flight = ref flights.Get(craft);
            ref var command = ref commands.Get(craft);
            ref readonly var mass = ref masses.Get(craft);

            var definition = rcs.Definition;
            var lever = part.LocalPosition - mass.CenterOfMass;

            if (flight.Mode != FlightMode.Physics || !controls.Get(craft).Rcs) {

                continue;

            }

            command.AuthorityNext += Authority(definition, lever);

            var rotationCommand = command.Rotation;
            var translationCommand = command.Translation;

            if (rotationCommand == double3.Zero && translationCommand == double3.Zero) {

                continue;

            }

            var bodyForce = double3.Zero;
            var bodyTorque = double3.Zero;
            var totalLevel = 0.0;
            var firing = 0u;

            for (var n = 0; n < definition.Nozzles.Length; n++) {

                var direction = part.LocalRotation * definition.Nozzles[n];
                var torqueAxis = double3.NormalizeOrZero(double3.Cross(lever, direction));
                var level = Math.Clamp(double3.Dot(direction, translationCommand) + double3.Dot(torqueAxis, rotationCommand), 0, 1);

                if (level < Deadband) {

                    continue;

                }

                totalLevel += level;
                bodyForce += direction * level;
                firing |= n < 32 ? 1u << n : 0;

            }

            if (totalLevel <= 0) {

                continue;

            }

            var survey = PropellantFeed.Survey(world, craft, part.FeedGroup, definition.Propellant, -1);
            var massFlow = definition.NozzleMassFlow * totalLevel;
            var needed = massFlow * dt;
            var supply = needed > 0 ? Math.Clamp(survey.Fuel / needed, 0, 1) : 0;

            PropellantFeed.Draw(world, craft, part.FeedGroup, definition.Propellant, needed * supply, survey.Fuel);

            var thrustPerLevel = Math.Max(0, definition.NozzleThrust - flight.Ambient.Pressure * definition.NozzleExitArea) * supply;

            bodyForce *= thrustPerLevel;
            bodyTorque = double3.Cross(lever, bodyForce);

            var rotation = transforms.Get(craft).Rotation;

            ref var craftForces = ref forces.Get(craft);

            craftForces.Force += rotation * bodyForce;
            craftForces.Torque += rotation * bodyTorque;

            rcs.MassFlow = massFlow * supply;
            rcs.Firing = supply > 0 ? firing : 0;
            rcs.Thrust = totalLevel * thrustPerLevel;
            flight.Thrust += rcs.Thrust;

        }

    }

    private static double3 Authority(RcsDefinition definition, double3 lever) {

        var arm = lever.Length() * definition.NozzleThrust;

        return new double3(arm, arm, arm);

    }

}

public sealed class ReactionWheelSystem : ISystem {

    public void Update(World world, in SimTime time) {

        var flights = world.Store<FlightState>();
        var commands = world.Store<AttitudeCommand>();
        var forces = world.Store<CraftForces>();
        var transforms = world.Store<Transform>();
        var vessels = world.Store<Vessel>();

        foreach (var row in world.Query<CommandPod, Part>()) {

            var craft = row.C2.Craft;
            var torque = row.C1.Definition.ReactionWheelTorque;

            if (torque <= 0 || flights.Get(craft).Mode != FlightMode.Physics || !vessels.Get(craft).Controllable) {

                continue;

            }

            ref var command = ref commands.Get(craft);

            command.AuthorityNext += new double3(torque, torque, torque);
            forces.Get(craft).Torque += transforms.Get(craft).Rotation * (command.Rotation * torque);

        }

    }

}
