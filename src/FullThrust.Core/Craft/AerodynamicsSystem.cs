using Brutal.Numerics;
using FullThrust.Core.Ecs;
using FullThrust.Core.Parts;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Craft;

// Drag acts at each part, so where area sits relative to the centre of mass decides stability.
public sealed class AerodynamicsSystem : ISystem {

    // Cross-flow drag of a cylinder broadside to the wind.
    public double SideDragCoefficient { get; set; } = 1.2;

    public void Update(World world, in SimTime time) {

        var flights = world.Store<FlightState>();
        var masses = world.Store<CraftMass>();
        var forces = world.Store<CraftForces>();
        var transforms = world.Store<Transform>();
        var velocities = world.Store<Velocity>();

        foreach (var row in world.Query<Part>()) {

            ref readonly var part = ref row.C1;

            var craft = part.Craft;

            ref var flight = ref flights.Get(craft);

            if (flight.Mode != FlightMode.Physics || flight.Ambient.Density <= 0) {

                continue;

            }

            var rotation = transforms.Get(craft).Rotation;
            var lever = rotation * (part.LocalPosition - masses.Get(craft).CenterOfMass);
            var air = flight.AirVelocity + double3.Cross(velocities.Get(craft).Angular, lever);
            var speed = air.Length();

            if (speed < 0.1) {

                continue;

            }

            var drag = DragForce(in part, rotation, air, speed, flight.Ambient.Density, flight.Mach);

            forces.Get(craft).AddAt(drag, lever);
            flight.Drag += drag.Length();

        }

    }

    public double3 DragForce(in Part part, doubleQuat craftRotation, double3 air, double speed, double density, double mach) {

        var axis = craftRotation * double3.UnitY;
        var cosine = double3.Dot(air, axis) / speed;

        // Moving nose-first means the top faces are the ones meeting the flow.
        var axialArea = cosine >= 0 ? part.Aero.TopArea : part.Aero.BottomArea;
        var cd = part.Definition.DragCoefficient;

        var area = cd * axialArea * Math.Abs(cosine) + SideDragCoefficient * part.Aero.SideArea * (1 - cosine * cosine);
        var q = 0.5 * density * speed * speed;

        return air * (-q * area * MachFactor(mach) / speed);

    }

    // Wave drag rises sharply through the transonic regime and eases off at high Mach.
    public static double MachFactor(double mach) {

        if (mach < 0.8) {

            return 1.0;

        }

        if (mach < 1.2) {

            return 1.0 + 1.2 * (mach - 0.8) / 0.4;

        }

        if (mach < 2.0) {

            return 2.2 - 0.6 * (mach - 1.2) / 0.8;

        }

        if (mach < 5.0) {

            return 1.6 - 0.4 * (mach - 2.0) / 3.0;

        }

        return 1.2;

    }

}
