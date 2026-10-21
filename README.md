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

Flags: `--no-vsync`, `--no-validation`, `--no-overlay`, `--rocket pinnacle|harrier|colossus`, `--scenario launchpad|lowOrbit|lunarTransfer`, `--focus N` (0 craft, 1 Earth, 2 Moon), `--camera-distance M`, `--camera-pitch DEG`, `--camera-yaw DEG`, `--time-scale X`. For inspecting visuals, `--headlight` lights the scene from over the camera's shoulder and `--terrain-levels` tints terrain chunks by LOD level (both also in the overlay).

Smoke tests: `--frames N` measures N frames after a 3 s warmup, logs timing, allocation, flight and SOI-transition stats, then exits. Add `--warp RATE`, `--throttle T`, `--stage N` (stage commands at start), `--auto-stage` (stage whenever every engine has burned out), `--sas` and `--capture-every K` (PNGs to `captures/`, or `--capture-dir`; frames just after each stage are captured too) for scripted visual checks, e.g.

```sh
dotnet run --project src/FullThrust.Game -- --rocket harrier --scenario launchpad --throttle 1 --stage 1 --sas --auto-stage --time-scale 8 --frames 3600 --capture-every 450 --camera-distance 140
```

In game, F12 saves a screenshot. Flight keys: Space stages, Shift/Ctrl throttle (Z full, X cut), W/S A/D Q/E attitude, T toggles SAS, R toggles RCS, I/K J/L H/N translate, P pauses.

## Craft and flight
Parts and resources are defined in `data/parts/parts.json`; the preset rockets selectable at launch are in `data/rockets/rockets.json`. Craft fly on patched-conic rails until they thrust, enter an atmosphere, approach a surface or separate stages, at which point they are simulated as Jolt rigid bodies in a double-precision frame that follows the focus craft.

## Rendering
Scene passes draw physically based (metallic-roughness) shading in linear HDR, pre-exposed for an EV100 camera, and a tonemap pass (ACES, AgX, Khronos PBR Neutral or Reinhard) resolves it to the swapchain before the overlay. The sun is defined in `data/celestial/earth-moon.json` (illuminance, colour and its yearly path), so planet rotation gives day and night. Exposure, tonemapper and ambient light are adjustable in the overlay.

Each frame runs in stages: offscreen preparation (sun shadow maps), a depth prepass, the opaque scene, transparent geometry, HDR post-processing, then the swapchain. Geometry implements `IDepthCaster` to take part in the prepass and shadows. The sun casts four cascaded shadow maps whose bounds are stable as the camera turns and texel-snapped as it moves; cascade count, distance, split, filtering and bias are tunable in the overlay. Ground-truth ambient occlusion (GTAO) runs as compute on the prepass depth after it, with a depth-aware filter, and darkens only ambient light. Compute effects build on `ComputeKernel` and `RenderTexture`, and publish results to every shader through descriptor set 0 (`GlobalBindings`). `--debug-view ao|cascades` swaps the shaded scene for a debug view in captures.

Craft parts are glTF models loaded with Brutal.Gltf, including base colour, metallic-roughness, normal, occlusion and emissive textures. Each part in `data/parts/parts.json` names its model and, for third-party models, how to orient and fit it to the part. Models under `data/models/parts` and `data/models/scene` are generated from `data/models/recipes.json` by the ModelGen tool. The engine bells and the Pinnacle capsule come from public-domain NASA 3D Resources models (see `data/models/nasa/NOTICE.md`).

```sh
dotnet run --project tools/FullThrust.ModelGen
dotnet run --project tools/FullThrust.ModelGen -- inspect data/models/nasa/ssme.glb
```

Planet and Moon surfaces are procedural, double-precision height fields defined per body in `earth-moon.json`: continents and oceans, or maria and craters, plus flattened sites such as the launch complex. A cube-sphere quadtree picks chunks by camera distance, worker threads build them, and skirts hide the seams between levels. The same height function drives the collision patch under the focus craft, so craft rest on the ground they are drawn on.

## License
The license is the [Unlicense](https://unlicense.org/), which allows for free use, modification, and distribution of the software.
