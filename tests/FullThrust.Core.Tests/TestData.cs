using FullThrust.Core.Bodies;
using FullThrust.Core.Orbits;

namespace FullThrust.Core.Tests;

internal static class TestData {

    public static string EarthMoonPath => Path.Combine(AppContext.BaseDirectory, "data", "celestial", "earth-moon.json");

    public static CelestialSystem EarthMoon() => CelestialSystemLoader.LoadFile(EarthMoonPath, new KeplerPropagator());

}
