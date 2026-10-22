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
dotnet build
dotnet test
dotnet run --project src/FullThrust.Game
```

## Running

The game can be run with various command line arguments to specify the rocket, scenario, and other parameters. For example:

```sh
dotnet run --project src/FullThrust.Game -- --rocket harrier --scenario launchpad --throttle 1 --stage 1 --sas --auto-stage --time-scale 8 --frames 3600 --capture-every 450 --camera-distance 140
```

## Tools

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
