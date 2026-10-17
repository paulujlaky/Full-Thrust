using FullThrust.Core.Ecs;
using FullThrust.Core.Parts;

namespace FullThrust.Core.Craft;

public struct FeedSurvey {

    public double Fuel;
    public double Oxidizer;

    // Lowest ullage pressure among the tanks that feed the consumer, which sets turbopump inlet conditions.
    public double InletPressure;

    public bool HasTanks;

}

// Tanks feed every consumer in the same craft and feed group, draining in proportion to what each holds.
public static class PropellantFeed {

    public static FeedSurvey Survey(World world, Entity craft, int group, int fuel, int oxidizer) {

        var survey = new FeedSurvey { InletPressure = double.PositiveInfinity };

        foreach (var row in world.Query<TankState, Part>()) {

            ref readonly var tank = ref row.C1;
            ref readonly var part = ref row.C2;

            if (part.Craft != craft || part.FeedGroup != group) {

                continue;

            }

            var capacity = tank.Definition.Capacity;
            var feeds = capacity[fuel] > 0 || (oxidizer >= 0 && capacity[oxidizer] > 0);

            if (!feeds) {

                continue;

            }

            survey.HasTanks = true;
            survey.Fuel += tank.Amount[fuel];
            survey.InletPressure = Math.Min(survey.InletPressure, tank.Pressure);

            if (oxidizer >= 0) {

                survey.Oxidizer += tank.Amount[oxidizer];

            }

        }

        if (!survey.HasTanks) {

            survey.InletPressure = 0;

        }

        return survey;

    }

    public static void Draw(World world, Entity craft, int group, int resource, double amount, double available) {

        if (amount <= 0 || available <= 0) {

            return;

        }

        var fraction = Math.Min(1.0, amount / available);

        foreach (var row in world.Query<TankState, Part>()) {

            ref var tank = ref row.C1;
            ref readonly var part = ref row.C2;

            if (part.Craft != craft || part.FeedGroup != group) {

                continue;

            }

            tank.Amount[resource] = Math.Max(0, tank.Amount[resource] - tank.Amount[resource] * fraction);

        }

    }

}
