#:project ../../src/FullThrust.Core/FullThrust.Core.csproj
using System.Diagnostics;
using System.Runtime;
using FullThrust.Core.Terrain;

// Terrain workers building chunks while the main thread forces compacting collections; see README, Known issues.
var seconds = args.Length > 0 ? double.Parse(args[0]) : 30;
var threads = args.Length > 1 ? int.Parse(args[1]) : 4;

// A hex mask pins the process to those logical processors, e.g. FFFF0000 for the E-cores of a 13900K.
if (args.Length > 2) {

    Process.GetCurrentProcess().ProcessorAffinity = (nint)Convert.ToInt64(args[2], 16);

}

var data = new TerrainData { Type = "continental", Seed = 7, Ocean = true };
var terrain = new ProceduralTerrain(data, 6_371_000);
var palette = new TerrainPalette(data);
var watch = Stopwatch.StartNew();
var built = 0L;

var workers = Enumerable.Range(0, threads).Select(t => new Thread(() => {

    var builder = new TerrainChunkBuilder();
    var vertices = new TerrainVertex[TerrainChunkBuilder.VertexCount];
    var random = new Random(t);

    while (watch.Elapsed.TotalSeconds < seconds) {

        var level = random.Next(4, 14);
        var node = new TerrainNode(random.Next(6), level, random.Next(1 << level), random.Next(1 << level));

        builder.Build(terrain, palette, 6_371_000, node, vertices);
        Interlocked.Increment(ref built);

    }

}) { IsBackground = true, Priority = ThreadPriority.BelowNormal }).ToArray();

foreach (var worker in workers) {

    worker.Start();

}

var keep = new List<byte[]>();
var collections = 0;

while (watch.Elapsed.TotalSeconds < seconds) {

    for (var i = 0; i < 2000; i++) {

        keep.Add(new byte[Random.Shared.Next(16, 4096)]);

    }

    keep.RemoveRange(0, keep.Count / 2);

    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    collections++;

    Thread.Sleep(5);

}

foreach (var worker in workers) {

    worker.Join();

}

Console.WriteLine($"{built} chunks on {threads} workers through {collections} forced collections without a crash.");
