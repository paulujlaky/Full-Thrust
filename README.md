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

Smoke tests: `--frames N` measures N frames after a 3 s warmup, logs timing, allocation, flight and SOI-transition stats, then exits. It also logs the mean GPU time of the frame and of every render pass (timestamp queries; the overlay's Frame Timing panel shows them live), and `--window WxH` sets the window size, e.g. `--window 2560x1440` for checking the 60 FPS target at 1440p. Add `--warp RATE`, `--throttle T`, `--stage N` (stage commands at start), `--auto-stage` (stage whenever every engine has burned out), `--sas` and `--capture-every K` (PNGs to `captures/`, or `--capture-dir`; frames just after each stage are captured too) for scripted visual checks, e.g.

```sh
dotnet run --project src/FullThrust.Game -- --rocket harrier --scenario launchpad --throttle 1 --stage 1 --sas --auto-stage --time-scale 8 --frames 3600 --capture-every 450 --camera-distance 140
```

In game, F12 saves a screenshot. Flight keys: Space stages, Shift/Ctrl throttle (Z full, X cut), W/S A/D Q/E attitude, T toggles SAS, R toggles RCS, I/K J/L H/N translate, P pauses.

## Craft and flight
Parts and resources are defined in `data/parts/parts.json`; the preset rockets selectable at launch are in `data/rockets/rockets.json`. Craft fly on patched-conic rails until they thrust, enter an atmosphere, approach a surface or separate stages, at which point they are simulated as Jolt rigid bodies in a double-precision frame that follows the focus craft.

## Rendering
Scene passes draw physically based (metallic-roughness) shading in linear HDR, pre-exposed for an EV100 camera, whose exposure adapts automatically to a centre-weighted measure of the scene's log luminance (read back from the GPU, adapting over about a second, never so far that night looks like day; `--ev100` pins it), and a tonemap pass (ACES, AgX, Khronos PBR Neutral or Reinhard) resolves it to the swapchain before the overlay. Physically based bloom (a 13-tap downsample chain and tent upsamples, after Jimenez) takes a few percent of the image before tonemapping. The sun is defined in `data/celestial/earth-moon.json` (illuminance, colour and its yearly path), so planet rotation gives day and night. Exposure, tonemapper and ambient light are adjustable in the overlay.

Each frame runs in stages: offscreen preparation (sun shadow maps), a depth prepass, the opaque scene, transparent geometry, HDR post-processing, then the swapchain. Geometry implements `IDepthCaster` to take part in the prepass and shadows. Planets with `scattering` data in their atmosphere get a physically based sky after Hillaire (2020): transmittance, multiple-scattering and sky-irradiance tables built once per body, and a sky-view table each frame, for Rayleigh, Mie and ozone. Sunlight on every surface is reddened by its path through the air, ambient light near the planet comes from the sky and the ground below, and from space the limb is ray-marched per pixel. Surfaces fade into the air with distance: near the ground through a froxel volume filled each frame by marching each screen column once, and from high above by marching each pixel down to its surface, which keeps the haze smooth from orbit. `--sun AZ,EL` places the sun relative to the local horizon for inspecting lighting. Participating media share one volumetric renderer: anything implementing `IVolumeSource` adds primitives (a kind, transform, scattering albedo, extinction and emission) each frame, and a half-resolution compute pass marches them in depth order against the scene, lit by the sun through the atmosphere with self-shadowing, approximate multiple scattering and sky ambient, then accumulates them over frames (a golden-ratio march offset each frame, history reprojected through where the volume was seen and clamped to the new frame's neighbourhood) and composites them back with a depth-aware upsample. Tileable Perlin-Worley and Worley 3D noise is generated once on the GPU for every volume to sample. `--debug-volume` puts a ball of smoke beside the craft. Engine plumes are volumes too: each burning engine gets a cone from its nozzle exit, sized by its exit area and stretched by throttle, which flares wide when the exhaust is far above ambient pressure (in vacuum) and shows shock diamonds in dense air. Their colour, brightness and soot come per fuel from `data/effects/plumes.json`. Each plume is also a point light (its core luminance over its visible area, in candela) that lights the craft, the pad and nearby volumes, with highlights widened by the light's size; where the exhaust strikes the ground it throws up a cloud of steam. Bodies with `clouds` data get a cumulus layer in the same framework (after Schneider's Nubis): a weather map that turns with the planet and drifts on the wind sets coverage and height, thinned or thickened by weather systems a thousand kilometres across, and Perlin-Worley billows eroded by Worley wisps fade with distance so the layer stays clean from orbit. The same weather map casts cloud shadows on the ground. Behind everything, a background pass draws procedural stars with real-sky magnitude counts and physical brightness (invisible at daylight exposures, as in photographs; `--ev100` lowers the exposure), a Milky Way placed in galactic coordinates, and the limb-darkened solar disc. The sun casts four cascaded shadow maps whose bounds are stable as the camera turns and texel-snapped as it moves; cascade count, distance, split, filtering and bias are tunable in the overlay. Ground-truth ambient occlusion (GTAO) runs as compute on the prepass depth after it, with a depth-aware filter, and darkens only ambient light. Compute effects build on `ComputeKernel` and `RenderTexture`, and publish results to every shader through descriptor set 0 (`GlobalBindings`). `--debug-view ao|cascades` swaps the shaded scene for a debug view in captures.

Craft parts are glTF models loaded with Brutal.Gltf, including base colour, metallic-roughness, normal, occlusion and emissive textures. Each part in `data/parts/parts.json` names its model and, for third-party models, how to orient and fit it to the part. Models under `data/models/parts` and `data/models/scene` are generated from `data/models/recipes.json` by the ModelGen tool. The engine bells and the Pinnacle capsule come from public-domain NASA 3D Resources models (see `data/models/nasa/NOTICE.md`).

```sh
dotnet run --project tools/FullThrust.ModelGen
dotnet run --project tools/FullThrust.ModelGen -- inspect data/models/nasa/ssme.glb
```

Planet and Moon surfaces are procedural, double-precision height fields defined per body in `earth-moon.json`: continents and oceans, or maria and craters, plus flattened sites such as the launch complex. A cube-sphere quadtree picks chunks by camera distance, worker threads build them, and skirts hide the seams between levels. The same height function drives the collision patch under the focus craft, so craft rest on the ground they are drawn on.

Up close, terrain blends detail materials over the palette colours: each body maps its palette channels (lowland, dry, rock, snow and so on) to CC0 texture sets under `data/textures/terrain` (see its NOTICE), sampled triplanar in the body-fixed frame at two tiling scales and height-blended between the three heaviest channels. Detail is normalised to each texture's mean colour, so it fades back to the palette at a distance. Each sample blends two shifted copies of its tile, picked by noise, so the tile grid never forms a lattice.

Oceans are drawn on the sea-level surface of the terrain: ten deep-water waves animated by the dispersion relation, with waves too small to resolve folded into roughness so the sun glint holds from orbit. The sea bed shows through the shallows, absorbed red first, and the shore gets wet sand and surf.

## License
The license is the [Unlicense](https://unlicense.org/), which allows for free use, modification, and distribution of the software.
