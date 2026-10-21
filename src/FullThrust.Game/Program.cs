using System.Globalization;
using Brutal.Logging;
using FullThrust.Game;
using FullThrust.Game.Scene;
using Microsoft.Extensions.Logging;

LogSystem.Initialize(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

var options = new GameOptions {

    Validation = !args.Contains("--no-validation"),
    VSync = !args.Contains("--no-vsync"),
    Overlay = !args.Contains("--no-overlay"),

};

string? Value(string name) {

    var index = Array.IndexOf(args, name);

    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;

}

double Number(string name, double fallback) => Value(name) is { } text ? double.Parse(text, CultureInfo.InvariantCulture) : fallback;

options.SmokeFrames = (int)Number("--frames", 0);

if (Value("--window")?.Split('x') is [var width, var height]) {

    options.WindowSize = new Brutal.Numerics.int2(int.Parse(width, CultureInfo.InvariantCulture), int.Parse(height, CultureInfo.InvariantCulture));

}

options.SmokeWarpRate = Number("--warp", 1);
options.SmokeThrottle = Number("--throttle", 0);
options.CaptureEvery = (int)Number("--capture-every", 0);
options.Focus = (int)Number("--focus", 0);
options.CameraDistance = Number("--camera-distance", 0);
options.CaptureDirectory = Value("--capture-dir") ?? options.CaptureDirectory;
options.CameraPitch = Number("--camera-pitch", double.NaN);
options.CameraYaw = Number("--camera-yaw", double.NaN);
options.TimeScale = Number("--time-scale", 1);
options.SmokeStages = (int)Number("--stage", 0);
options.SmokeAutoStage = args.Contains("--auto-stage");
options.SmokeSas = args.Contains("--sas");
options.Headlight = args.Contains("--headlight");
options.ShowTerrainLevels = args.Contains("--terrain-levels");
options.DebugView = Value("--debug-view");
options.Ev100 = Number("--ev100", double.NaN);
options.Sun = Value("--sun");
options.DebugVolume = args.Contains("--debug-volume");
options.Rocket = Value("--rocket");

if (Value("--scenario") is { } scenario) {

    options.Scenario = Enum.Parse<Scenario>(scenario, ignoreCase: true);

}

using var game = new GameApp(options);

game.Run();
