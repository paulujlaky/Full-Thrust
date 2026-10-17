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

Flags: `--no-vsync`, `--no-validation`, `--no-overlay`, `--rocket pinnacle|harrier|colossus`, `--scenario launchpad|lowOrbit|lunarTransfer`, `--focus N` (0 craft, 1 Earth, 2 Moon), `--camera-distance M`, `--camera-pitch DEG`, `--time-scale X`.

Smoke tests: `--frames N` measures N frames after a 3 s warmup, logs timing, allocation, flight and SOI-transition stats, then exits. Add `--warp RATE`, `--throttle T`, `--stage N` (stage commands at start), `--auto-stage` (stage whenever every engine has burned out), `--sas` and `--capture-every K` (PNGs to `captures/`, or `--capture-dir`; frames just after each stage are captured too) for scripted visual checks, e.g.

```sh
dotnet run --project src/FullThrust.Game -- --rocket harrier --scenario launchpad --throttle 1 --stage 1 --sas --auto-stage --time-scale 8 --frames 3600 --capture-every 450 --camera-distance 140
```

In game, F12 saves a screenshot. Flight keys: Space stages, Shift/Ctrl throttle (Z full, X cut), W/S A/D Q/E attitude, T toggles SAS, R toggles RCS, I/K J/L H/N translate, P pauses.

## Craft and flight
Parts and resources are defined in `data/parts/parts.json`; the preset rockets selectable at launch are in `data/rockets/rockets.json`. Craft fly on patched-conic rails until they thrust, enter an atmosphere, approach a surface or separate stages, at which point they are simulated as Jolt rigid bodies in a double-precision frame that follows the focus craft.

## License
The license is the [Unlicense](https://unlicense.org/), which allows for free use, modification, and distribution of the software.
