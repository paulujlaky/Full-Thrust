using Brutal.Logging;
using FullThrust.Game;
using Microsoft.Extensions.Logging;

LogSystem.Initialize(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

var options = new GameOptions {

    Validation = !args.Contains("--no-validation"),
    VSync = !args.Contains("--no-vsync"),
    Overlay = !args.Contains("--no-overlay"),

};

var framesIndex = Array.IndexOf(args, "--frames");

if (framesIndex >= 0 && framesIndex + 1 < args.Length) {

    options.SmokeFrames = int.Parse(args[framesIndex + 1]);

}

using var game = new GameApp(options);

game.Run();
