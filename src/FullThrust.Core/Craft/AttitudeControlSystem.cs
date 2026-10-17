using Brutal.Numerics;
using FullThrust.Core.Ecs;
using FullThrust.Core.Simulation;
using FullThrust.Core.Space;

namespace FullThrust.Core.Craft;

// Turns pilot input into a normalised body-axis command; with SAS on it holds the attitude the pilot left it at.
public sealed class AttitudeControlSystem : ISystem {

    public double NaturalFrequency { get; set; } = 1.2;

    public double DampingRatio { get; set; } = 1.0;

    public void Update(World world, in SimTime time) {

        var transforms = world.Store<Transform>();
        var velocities = world.Store<Velocity>();
        var masses = world.Store<CraftMass>();

        foreach (var row in world.Query<AttitudeCommand, CraftControl, Vessel>()) {

            ref var command = ref row.C1;
            ref readonly var control = ref row.C2;

            command.Authority = command.AuthorityNext;
            command.AuthorityNext = double3.Zero;

            if (!row.C3.Controllable) {

                command.Rotation = double3.Zero;
                command.Translation = double3.Zero;
                command.Holding = false;

                continue;

            }

            var rotation = transforms.Get(row.Entity).Rotation;
            var inverse = doubleQuat.Conjugate(rotation);
            var rate = inverse * velocities.Get(row.Entity).Angular;
            var input = new double3(control.Pitch, control.Roll, control.Yaw);

            command.Translation = control.Rcs ? new double3(control.Translation.X, control.Translation.Y, control.Translation.Z) : double3.Zero;

            if (!control.Sas) {

                command.Holding = false;
                command.Rotation = input;

                continue;

            }

            var acceleration = MaxAngularAcceleration(command.Authority, masses.Get(row.Entity));

            if (control.HasRotationInput) {

                command.Holding = false;
                command.Rotation = input + Damp(-rate, acceleration) * Mask(input);

                continue;

            }

            if (!command.Holding) {

                command.Holding = true;
                command.HoldTarget = rotation;

            }

            var error = AxisAngle(inverse * command.HoldTarget);
            var desired = error * (NaturalFrequency * NaturalFrequency) - rate * (2 * DampingRatio * NaturalFrequency);

            command.Rotation = Clamp(desired / acceleration);

        }

    }

    private double3 Damp(double3 negativeRate, double3 acceleration) => Clamp(negativeRate * (2 * DampingRatio * NaturalFrequency) / acceleration);

    private static double3 Mask(double3 input) => new(input.X == 0 ? 1 : 0, input.Y == 0 ? 1 : 0, input.Z == 0 ? 1 : 0);

    private static double3 MaxAngularAcceleration(double3 authority, in CraftMass mass) {

        var inertia = mass.Inertia.Diagonal;

        return new double3(Safe(authority.X, inertia.X), Safe(authority.Y, inertia.Y), Safe(authority.Z, inertia.Z));

    }

    private static double Safe(double torque, double inertia) => inertia > 0 ? Math.Max(torque / inertia, 1e-3) : 1e-3;

    private static double3 Clamp(double3 v) => new(Math.Clamp(v.X, -1, 1), Math.Clamp(v.Y, -1, 1), Math.Clamp(v.Z, -1, 1));

    private static double3 AxisAngle(doubleQuat q) {

        if (q.W < 0) {

            q = -q;

        }

        var sinHalf = Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z);

        if (sinHalf < 1e-9) {

            return double3.Zero;

        }

        var angle = 2 * Math.Atan2(sinHalf, q.W);

        return new double3(q.X, q.Y, q.Z) * (angle / sinHalf);

    }

}
