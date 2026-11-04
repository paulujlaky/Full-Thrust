# Full-Thrust
A spaceflight simulation game in the spirit of Kerbal Space Program, developed off of the BRUTAL custom-engine framework.

## Planned Features
- Realistic orbital mechanics and physics simulation
- Customizable spacecraft design and construction
- Mission/node planning and execution
- High-fidelity graphics and effects

## Later Features
- Multiplayer support

## Motivation
Many spaceflight simulation games exist, but few are high fidelity and realistic. Full-Thrust aims to be both visually striking as well as physically
accurate, including realistic pneumatics, engine characteristics, and mechanics. The goal is to create a game that is equally fun and educational.

## Development Status
The game is currently in the early stages of development.

## Building
Requires the .NET 10 SDK and a Vulkan 1.3 GPU on Windows. Install the LunarG Vulkan SDK to enable validation layers.

```sh
sh build.sh
dotnet test
sh run.sh
```

## Running

From Git Bash, use `sh build.sh -c Release` for a Release build. The default build and run configuration is Debug. Pass game flags directly to `sh run.sh`.

The game can be run with various command line arguments to specify the rocket, scenario, and other parameters. For example:

```sh
sh run.sh --rocket harrier --scenario launchpad --throttle 1 --stage 1 --sas --auto-stage --time-scale 8 --frames 3600 --capture-every 450 --camera-distance 140
```

## Tools

On Windows, `./tools/Capture-Visuals.ps1` builds Release once and captures the launchpad, liftoff, and capsule views. Select other views and resolution with `./tools/Capture-Visuals.ps1 -View staging,earth,moon,reentry -Window 2560x1440`. Each view writes one PNG under `captures/visual-review`. Use `-Output` to keep a separate comparison run.

Use the `engines` and `night-pad` views to inspect nozzle alignment and pad lighting. In the game command line, `--camera-part engine-harrier-9` places the camera pivot on that part of the selected rocket.

For a repeatable liftoff view, run the following command. After warmup each rendered frame advances the simulation by 1/60 of a second. The game writes the final frame and reports measured GPU time.

```sh
dotnet run -c Release --project src/FullThrust.Game -- --rocket harrier --scenario launchpad --stage 1 --throttle 1 --sas --camera-distance 110 --camera-yaw 65 --camera-pitch -8 --frames 300 --capture-fps 60 --capture-last --capture-dir captures/liftoff --window 1280x720 --no-overlay --no-vsync
```

To stress test the garbage collector, run the following command:

```sh
dotnet run tools/GcStress/GcStress.cs -- 15
```

To generate models from GLB files, run the following command:

```sh
dotnet run --project tools/FullThrust.ModelGen
```

## License
The license is the [Unlicense](https://unlicense.org/), which allows for free use, modification, and distribution of the software.
