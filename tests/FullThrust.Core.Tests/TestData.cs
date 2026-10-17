using FullThrust.Core.Bodies;
using FullThrust.Core.Craft;
using FullThrust.Core.Orbits;
using FullThrust.Core.Parts;

namespace FullThrust.Core.Tests;

internal static class TestData {

    public static string EarthMoonPath => Path.Combine(AppContext.BaseDirectory, "data", "celestial", "earth-moon.json");

    public static string PartsPath => Path.Combine(AppContext.BaseDirectory, "data", "parts", "parts.json");

    public static string RocketsPath => Path.Combine(AppContext.BaseDirectory, "data", "rockets", "rockets.json");

    public static CelestialSystem EarthMoon() => CelestialSystemLoader.LoadFile(EarthMoonPath, new KeplerPropagator());

    public static PartCatalog Parts() => PartCatalogLoader.LoadFile(PartsPath);

    public static RocketCatalog Rockets(PartCatalog parts) => RocketCatalogLoader.LoadFile(RocketsPath, parts);

}
