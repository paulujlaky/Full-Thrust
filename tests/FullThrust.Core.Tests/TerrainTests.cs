using Brutal.Numerics;
using FullThrust.Core.Bodies;
using FullThrust.Core.Craft;
using FullThrust.Core.Terrain;

namespace FullThrust.Core.Tests;

public class TerrainTests {

    private static readonly CelestialSystem System = TestData.EarthMoon();

    private static CelestialBody Earth => System.Root;

    private static CelestialBody Moon => System.Find("Moon")!;

    private static IEnumerable<double3> Directions(int count, int seed) {

        var random = new Random(seed);

        for (var i = 0; i < count; i++) {

            var z = random.NextDouble() * 2 - 1;
            var angle = random.NextDouble() * Math.Tau;
            var r = Math.Sqrt(1 - z * z);

            yield return new double3(r * Math.Cos(angle), z, r * Math.Sin(angle));

        }

    }

    [Fact]
    public void HeightsAreDeterministicAndContinuous() {

        var a = new ProceduralTerrain(Earth.TerrainData!, Earth.Radius);
        var b = new ProceduralTerrain(Earth.TerrainData!, Earth.Radius);

        foreach (var direction in Directions(200, 1)) {

            var h = a.Height(direction);

            Assert.Equal(h, b.Height(direction));

            // One metre along the surface never moves the ground by more than a steep cliff would.
            var tangent = double3.Normalize(double3.Cross(direction, double3.UnitY));
            var step = double3.Normalize(direction + tangent / Earth.Radius);

            Assert.True(Math.Abs(a.Height(step) - h) < 3, $"jump of {Math.Abs(a.Height(step) - h):F2} m");

        }

    }

    [Fact]
    public void HeightsStayWithinTheAdvertisedBounds() {

        foreach (var body in new[] { Earth, Moon }) {

            var terrain = body.Terrain!;

            foreach (var direction in Directions(2000, 2)) {

                var h = terrain.Height(direction, 50);

                Assert.InRange(h, terrain.MinHeight, terrain.MaxHeight);

            }

        }

    }

    [Fact]
    public void EarthHasOceansAndContinents() {

        var land = 0;

        foreach (var direction in Directions(4000, 3)) {

            if (Earth.Terrain!.Height(direction, 5000) > 0) {

                land++;

            }

        }

        Assert.InRange(land / 4000.0, 0.2, 0.45);

    }

    [Fact]
    public void CoarseResolutionOnlyDropsFineDetail() {

        foreach (var body in new[] { Earth, Moon }) {

            foreach (var direction in Directions(300, 4)) {

                var fine = body.Terrain!.Height(direction, 1);
                var coarse = body.Terrain!.Height(direction, 2000);

                Assert.True(Math.Abs(fine - coarse) < 400, $"{body.Name}: {fine:F0} vs {coarse:F0}");

            }

        }

    }

    [Fact]
    public void LaunchSiteIsFlatAndAboveTheSea() {

        var terrain = Earth.Terrain!;
        var site = terrain.Sites[0];

        Assert.True(site.Height >= 6);

        var east = double3.Normalize(double3.Cross(double3.UnitY, site.Direction));
        var north = double3.Cross(site.Direction, east);

        for (var i = 0; i < 32; i++) {

            var angle = i * Math.Tau / 32;
            var offset = (east * Math.Cos(angle) + north * Math.Sin(angle)) * (site.Radius * 0.95 / Earth.Radius);

            Assert.Equal(site.Height, terrain.Height(double3.Normalize(site.Direction + offset), 0.25), 1e-6);

        }

    }

    [Fact]
    public void CraftOnThePadStartsOnTheGround() {

        var parts = TestData.Parts();
        var rocket = TestData.Rockets(parts).Find("pinnacle")!;
        var site = Earth.Terrain!.Sites[0];
        var placement = CraftPlacement.OnSurface(System, Earth, site.Latitude, site.Longitude, rocket, 0);

        var bottom = placement.Pose.Position + placement.Pose.Rotation * new double3(0, rocket.Bottom, 0);

        Assert.Equal(0.05, System.SurfaceAltitude(Earth.Index, bottom, 0), 0.01);

    }

    [Fact]
    public void ChunkEdgesMatchTheirNeighbours() {

        var palette = new TerrainPalette(Moon.TerrainData!);
        var builder = new TerrainChunkBuilder();
        var left = new TerrainVertex[TerrainChunkBuilder.VertexCount];
        var right = new TerrainVertex[TerrainChunkBuilder.VertexCount];

        var a = builder.Build(Moon.Terrain!, palette, Moon.Radius, new TerrainNode(4, 6, 20, 31), left);
        var b = builder.Build(Moon.Terrain!, palette, Moon.Radius, new TerrainNode(4, 6, 21, 31), right);

        const int side = TerrainChunkBuilder.Side;

        for (var y = 0; y < side; y++) {

            var p = a.Center + Double(left[y * side + side - 1].Position);
            var q = b.Center + Double(right[y * side].Position);

            Assert.True((p - q).Length() < 0.5, $"row {y}: edges {(p - q).Length():F3} m apart");

        }

    }

    [Fact]
    public void PaletteWeightsReproduceTheSurfaceColour() {

        foreach (var body in new[] { Earth, Moon }) {

            var palette = new TerrainPalette(body.TerrainData!);

            foreach (var direction in Directions(200, 7)) {

                var sample = body.Terrain!.Sample(direction, 100);
                var surface = palette.Surface(sample, direction, 0.2f, out var weights);

                var sum = 0.0f;
                var color = float3.Zero;

                for (var i = 0; i < TerrainWeights.Count; i++) {

                    sum += weights[i];
                    color += palette.ChannelColor((TerrainChannel)i) * weights[i];

                }

                Assert.Equal(1.0f, sum, 4);
                Assert.Equal(surface.X, color.X, 4);
                Assert.Equal(surface.Y, color.Y, 4);
                Assert.Equal(surface.Z, color.Z, 4);

            }

        }

    }

    [Fact]
    public void SkirtsHangBelowTheChunkEdges() {

        var palette = new TerrainPalette(Earth.TerrainData!);
        var vertices = new TerrainVertex[TerrainChunkBuilder.VertexCount];
        var bounds = new TerrainChunkBuilder().Build(Earth.Terrain!, palette, Earth.Radius, new TerrainNode(2, 9, 100, 200), vertices);

        for (var i = TerrainChunkBuilder.GridVertices; i < TerrainChunkBuilder.VertexCount; i++) {

            var skirt = (bounds.Center + Double(vertices[i].Position)).Length();

            Assert.True(skirt < Earth.Radius + Earth.Terrain!.MaxHeight);

        }

        Assert.All(TerrainChunkBuilder.Indices, index => Assert.InRange(index, 0, TerrainChunkBuilder.VertexCount - 1));

    }

    [Fact]
    public void QuadtreeCoversTheSphereFromAfar() {

        var source = new AllReady(Earth.Radius);
        var quadtree = new TerrainQuadtree(Earth.Terrain!, Earth.Radius, TerrainChunkBuilder.Quads);

        quadtree.Update(new double3(0, 0, 40 * Earth.Radius), source);

        var area = 0.0;

        foreach (var node in quadtree.Selected) {

            area += node.Size * node.Size;

        }

        // Each face has area 4; from this far only the face fully behind the planet is beyond the horizon.
        Assert.InRange(area, 8, 20);
        Assert.True(quadtree.Culled >= 1);
        Assert.True(quadtree.DeepestLevel <= 2);

    }

    [Fact]
    public void QuadtreeRefinesTowardsTheCamera() {

        var source = new AllReady(Earth.Radius);
        var quadtree = new TerrainQuadtree(Earth.Terrain!, Earth.Radius, TerrainChunkBuilder.Quads);
        var site = Earth.Terrain!.Sites[0];

        quadtree.Update(site.Direction * (Earth.Radius + site.Height + 2), source);

        Assert.Equal(quadtree.MaxLevel, quadtree.DeepestLevel);
        Assert.InRange(quadtree.Count, 50, 1500);

    }

    [Fact]
    public void QuadtreeDrawsParentsUntilChildrenAreBuilt() {

        var source = new RootsOnly();
        var quadtree = new TerrainQuadtree(Earth.Terrain!, Earth.Radius, TerrainChunkBuilder.Quads);

        quadtree.Update(new double3(Earth.Radius + 1000, 0, 0), source);

        Assert.Equal(6, quadtree.Count + quadtree.Culled);
        Assert.True(source.Requests is > 0 and <= 24);

    }

    [Fact]
    public void TerrainSamplingAndSelectionDoNotAllocate() {

        var palette = new TerrainPalette(Moon.TerrainData!);
        var builder = new TerrainChunkBuilder();
        var vertices = new TerrainVertex[TerrainChunkBuilder.VertexCount];
        var quadtree = new TerrainQuadtree(Earth.Terrain!, Earth.Radius, TerrainChunkBuilder.Quads);
        var source = new AllReady(Earth.Radius);
        var camera = Earth.Terrain!.Sites[0].Direction * (Earth.Radius + 500);

        builder.Build(Moon.Terrain!, palette, Moon.Radius, new TerrainNode(1, 8, 3, 4), vertices);
        quadtree.Update(camera, source);

        var before = GC.GetAllocatedBytesForCurrentThread();

        builder.Build(Moon.Terrain!, palette, Moon.Radius, new TerrainNode(1, 8, 3, 5), vertices);
        quadtree.Update(camera, source);
        System.SurfaceAltitude(Earth.Index, camera, 0);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

    }

    [Fact]
    public void MoonCratersHaveNoCliffsAlongCellBoundaries() {

        const double step = 50;

        foreach (var (latitude, longitude, heading) in new[] { (0.1, 0.3, 0.0), (-0.4, 1.2, 1.1), (0.7, -2.0, 2.3), (0.02, 2.9, 0.8) }) {

            var origin = CelestialSystem.BodyFixedPoint(Moon, latitude, longitude, 0) / Moon.Radius;
            var east = double3.Normalize(double3.Cross(double3.UnitY, origin));
            var north = double3.Cross(origin, east);
            var direction = east * Math.Cos(heading) + north * Math.Sin(heading);
            var previous = Moon.GroundHeight(origin, step);

            for (var i = 1; i < 6000; i++) {

                var angle = i * step / Moon.Radius;
                var point = origin * Math.Cos(angle) + direction * Math.Sin(angle);
                var height = Moon.GroundHeight(point, step);

                Assert.True(Math.Abs(height - previous) < 1.5 * step, $"{Math.Abs(height - previous):F0} m jump at step {i} from ({latitude}, {longitude})");

                previous = height;

            }

        }

    }

    // The least sloping of a grid of sites around a point, so resting tests do not depend on where craters fall.
    private static (double Latitude, double Longitude) FlattestSite(CelestialBody body, double latitude, double longitude) {

        var best = (latitude, longitude);
        var bestSlope = double.MaxValue;
        var step = 200.0 / body.Radius;

        for (var i = -5; i <= 5; i++) {

            for (var j = -5; j <= 5; j++) {

                var lat = latitude + i * step;
                var lon = longitude + j * step;
                var slope = 0.0;

                foreach (var (dLat, dLon) in (ReadOnlySpan<(double, double)>)[(1, 0), (-1, 0), (0, 1), (0, -1)]) {

                    var around = CelestialSystem.BodyFixedPoint(body, lat + dLat * 5 / body.Radius, lon + dLon * 5 / body.Radius, 0);
                    var centre = CelestialSystem.BodyFixedPoint(body, lat, lon, 0);

                    slope = Math.Max(slope, Math.Abs(body.GroundHeight(double3.Normalize(around), 0.5) - body.GroundHeight(double3.Normalize(centre), 0.5)));

                }

                if (slope < bestSlope) {

                    best = (lat, lon);
                    bestSlope = slope;

                }

            }

        }

        return best;

    }

    [Fact]
    public void RocketRestsOnTheMoonsTerrain() {

        using var harness = new FlightHarness();

        var moon = harness.Moon;
        var (latitude, longitude) = FlattestSite(moon, 0.2, 0.4);
        var craft = harness.SpawnOnSurface("pinnacle", moon, latitude, longitude);

        harness.Run(3);

        ref readonly var flight = ref harness.Get<FlightState>(craft);
        ref readonly var transform = ref harness.Get<Space.Transform>(craft);

        var bottom = transform.Position + transform.Rotation * new double3(0, harness.Rocket("pinnacle").Bottom, 0);
        var clearance = harness.System.SurfaceAltitude(moon.Index, bottom, harness.Clock.UniversalTime, 0.5);

        Assert.Equal(moon.Index, flight.Body);
        Assert.Equal(FlightMode.Physics, flight.Mode);
        Assert.True(clearance > -0.5, $"sank {-clearance:F2} m into the ground");
        Assert.True(flight.AirVelocity.Length() < 0.5, $"sliding at {flight.AirVelocity.Length():F2} m/s");

    }

    [Fact]
    public void GroundPatchFollowsTheTerrainUnderTheCraft() {

        using var harness = new FlightHarness();

        var craft = harness.SpawnOnSurface("pinnacle", harness.Moon, -0.3, 1.1);

        harness.Run(1);

        var ground = harness.Flight.Ground;
        var patch = harness.Get<GroundPatch>(ground.Ground);
        var bodyFixed = harness.System.WorldToBodyFixed(patch.Body, harness.Get<Space.Transform>(craft).Position, harness.Clock.UniversalTime);
        var direction = double3.Normalize(bodyFixed);
        var expected = (harness.Moon.Radius + harness.Moon.GroundHeight(direction, 3)) - harness.Moon.Radius;
        var surface = direction * (harness.Moon.Radius + expected);

        Assert.Equal(double3.Dot(surface - patch.Anchor, double3.Normalize(patch.Anchor)), ground.PatchHeightAt(patch, surface), 1.5);

    }

    private static double3 Double(float3 v) => new(v.X, v.Y, v.Z);

    private sealed class AllReady : ITerrainChunkSource {

        private readonly double _radius;

        public AllReady(double radius) {

            _radius = radius;

        }

        public bool TryGetBounds(in TerrainNode node, out TerrainBounds bounds) {

            bounds = new TerrainBounds(CubeSphere.Center(node) * _radius, CubeSphere.EdgeLength(node, _radius) * 0.75);

            return true;

        }

        public bool Request(in TerrainNode node) => false;

    }

    private sealed class RootsOnly : ITerrainChunkSource {

        public int Requests { get; private set; }

        public bool TryGetBounds(in TerrainNode node, out TerrainBounds bounds) {

            bounds = new TerrainBounds(CubeSphere.Center(node) * 6371000, 1e7);

            return node.Level == 0;

        }

        public bool Request(in TerrainNode node) {

            Requests++;

            return true;

        }

    }

}
