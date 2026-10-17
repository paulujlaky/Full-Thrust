using FullThrust.Core.Craft;
using FullThrust.Core.Parts;

namespace FullThrust.Core.Tests;

public class PartCatalogTests {

    [Fact]
    public void CatalogProvidesEveryRequiredPartType() {

        var parts = TestData.Parts();

        Assert.Contains(parts.Parts, p => p.Module<CommandDefinition>() is not null);
        Assert.Contains(parts.Parts, p => p.Module<EngineDefinition>() is not null);
        Assert.Contains(parts.Parts, p => p.Module<TankDefinition>() is not null && p.Module<CommandDefinition>() is null);
        Assert.Contains(parts.Parts, p => p.Module<DecouplerDefinition>() is not null);
        Assert.Contains(parts.Parts, p => p.Module<RcsDefinition>() is not null);

    }

    [Fact]
    public void EngineThrustInterpolatesBetweenVacuumAndSeaLevel() {

        var engine = TestData.Parts()["engine-harrier-9"].Module<EngineDefinition>()!;
        var flow = engine.MaxMassFlow;

        Assert.Equal(engine.VacuumThrust, engine.ThrustAt(flow, 0), 1e-6);
        Assert.Equal(engine.VacuumThrust * engine.SeaLevelIsp / engine.VacuumIsp, engine.ThrustAt(flow, 101325), 1e-3);
        Assert.Equal(0, engine.ThrustAt(flow, 1e9));

    }

    [Fact]
    public void TanksReserveUllageAboveTheirPropellant() {

        var tank = TestData.Parts()["tank-harrier-1"].Module<TankDefinition>()!;
        var parts = TestData.Parts();

        var lox = parts.FindResource("LOX")!;
        var rp1 = parts.FindResource("RP-1")!;

        var propellantVolume = tank.Capacity[lox.Id] / lox.Density + tank.Capacity[rp1.Id] / rp1.Density;

        Assert.Equal(propellantVolume / 0.97, tank.Volume, 1e-6);

    }

    [Theory]
    [InlineData("pinnacle", 2)]
    [InlineData("harrier", 3)]
    [InlineData("colossus", 5)]
    public void PresetsStackWithoutGaps(string id, int stages) {

        var rocket = TestData.Rockets(TestData.Parts()).Find(id)!;

        Assert.Equal(stages, rocket.StageCount);

        foreach (var part in rocket.Parts) {

            if (part.Parent < 0 || part.Radial) {

                continue;

            }

            var parent = rocket.Parts[part.Parent];
            var parentBottom = parent.Position.Y - parent.Definition.Length / 2;
            var top = part.Position.Y + part.Definition.Length / 2;

            Assert.Equal(parentBottom, top, 1e-9);

        }

    }

    [Fact]
    public void RadialPartsAreMirroredAroundTheirParent() {

        var rocket = TestData.Rockets(TestData.Parts()).Find("pinnacle")!;
        var quads = rocket.Parts.Where(p => p.Radial).ToArray();

        Assert.Equal(4, quads.Length);

        var sum = quads.Aggregate(Brutal.Numerics.double3.Zero, (acc, p) => acc + p.Position);

        Assert.True(Math.Abs(sum.X) < 1e-9 && Math.Abs(sum.Z) < 1e-9);

    }

    [Fact]
    public void PresetMassesMatchTheirReferenceVehicles() {

        var rockets = TestData.Rockets(TestData.Parts());

        Assert.InRange(rockets.Find("pinnacle")!.TotalMass, 20_000, 35_000);
        Assert.InRange(rockets.Find("harrier")!.TotalMass, 500_000, 600_000);
        Assert.InRange(rockets.Find("colossus")!.TotalMass, 2_800_000, 3_100_000);

    }

    [Fact]
    public void InvalidPresetsAreRejected() {

        var parts = TestData.Parts();

        var unknown = new RocketPresetData { Id = "bad", Parts = [new RocketPartData { Part = "no-such-part" }] };
        var radialRoot = new RocketPresetData { Id = "bad", Parts = [new RocketPartData { Part = "rcs-quad", Radial = 4 }] };

        Assert.Throws<KeyNotFoundException>(() => RocketCatalogLoader.Build(unknown, parts));
        Assert.Throws<InvalidDataException>(() => RocketCatalogLoader.Build(radialRoot, parts));

    }

}
