using FullThrust.Core.Atmosphere;
using FullThrust.Core.Ecs;
using FullThrust.Core.Parts;
using FullThrust.Core.Simulation;

namespace FullThrust.Core.Craft;

// Regulated helium holds tank pressure until it runs out, after which the ullage gas blows down.
public sealed class TankPressureSystem : ISystem {

    public void Update(World world, in SimTime time) {

        foreach (var row in world.Query<TankState>()) {

            Pressurize(ref row.C1);

        }

    }

    public static void Pressurize(ref TankState tank) {

        var ullage = tank.Definition.Volume - tank.PropellantVolume;
        var previous = tank.UllageVolume;

        tank.UllageVolume = ullage;

        if (ullage <= previous || previous <= 0) {

            return;

        }

        var gas = PhysicalConstants.HeliumMolarMass / (LayeredAtmosphere.GasConstant * PhysicalConstants.PressurantTemperature);
        var regulated = tank.Definition.Pressure;

        if (tank.PressurantMass > 0 && tank.Pressure <= regulated * 1.000001) {

            var needed = (regulated * ullage - tank.Pressure * previous) * gas;

            if (needed <= tank.PressurantMass) {

                tank.PressurantMass -= Math.Max(0, needed);
                tank.Pressure = regulated;

                return;

            }

            var added = tank.PressurantMass;

            tank.PressurantMass = 0;
            tank.Pressure = (tank.Pressure * previous + added / gas) / ullage;

            return;

        }

        tank.Pressure *= previous / ullage;

    }

}
