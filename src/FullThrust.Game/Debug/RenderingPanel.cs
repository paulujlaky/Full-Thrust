using Brutal.ImGuiApi;
using Brutal.Numerics;
using FullThrust.Game.Rendering;
using FullThrust.Game.Terrain;

namespace FullThrust.Game.Debug;

// Terrain LOD and mesh statistics, plus the terrain debug switches.
public sealed class RenderingPanel : IDebugPanel {

    private readonly TerrainRenderer _terrain;
    private readonly MeshPass _meshes;
    private readonly ModelLibrary _models;
    private readonly SceneLighting _lighting;
    private readonly ShadowMaps _shadows;
    private readonly AmbientOcclusion _occlusion;
    private readonly BackgroundPass _background;
    private readonly AtmosphereRenderer _atmosphere;
    private readonly ExposureController _exposure;
    private readonly BloomPass _bloom;

    public RenderingPanel(TerrainRenderer terrain, MeshPass meshes, ModelLibrary models, SceneLighting lighting, ShadowMaps shadows, AmbientOcclusion occlusion, BackgroundPass background, AtmosphereRenderer atmosphere, ExposureController exposure, BloomPass bloom) {

        _terrain = terrain;
        _meshes = meshes;
        _models = models;
        _lighting = lighting;
        _shadows = shadows;
        _occlusion = occlusion;
        _background = background;
        _atmosphere = atmosphere;
        _exposure = exposure;
        _bloom = bloom;

    }

    public string Title => "Rendering";

    public bool Visible { get; set; } = true;

    private void DrawCamera() {

        ImGui.SeparatorText("Camera"u8);

        var automatic = _exposure.Automatic;

        if (ImGui.Checkbox("Auto exposure"u8, ref automatic)) {

            _exposure.Automatic = automatic;

        }

        ImGui.SameLine();
        ImGui.Text($"EV100 {_lighting.Ev100:F1}, scene {_exposure.MeasuredLuminance:G3} cd/m2");

        var compensation = _exposure.Compensation;

        if (ImGui.SliderFloat("Compensation"u8, ref compensation, -4.0f, 4.0f, "%.1f EV"u8)) {

            _exposure.Compensation = compensation;

        }

        var adaptation = _exposure.AdaptationTime;

        if (ImGui.SliderFloat("Adaptation"u8, ref adaptation, 0.05f, 5.0f, "%.2f s"u8)) {

            _exposure.AdaptationTime = adaptation;

        }

        var bloom = _bloom.Intensity;

        if (ImGui.SliderFloat("Bloom"u8, ref bloom, 0.0f, 0.3f, "%.3f"u8)) {

            _bloom.Intensity = bloom;

        }

    }

    private void DrawBackground() {

        ImGui.SeparatorText("Sky"u8);

        var atmosphere = _atmosphere.Active;

        if (ImGui.Checkbox("Atmosphere"u8, ref atmosphere)) {

            _atmosphere.Active = atmosphere;

        }

        ImGui.SameLine();
        ImGui.Text($"{_atmosphere.Body?.Name ?? "none"}");

        var stars = _background.StarBrightness;

        if (ImGui.SliderFloat("Star brightness"u8, ref stars, 1.0f, 100000.0f, "%.0fx"u8, ImGuiSliderFlags.Logarithmic)) {

            _background.StarBrightness = stars;

        }

        var galaxy = _background.MilkyWayBrightness;

        if (ImGui.SliderFloat("Milky Way"u8, ref galaxy, 0.0f, 100000.0f, "%.0fx"u8, ImGuiSliderFlags.Logarithmic)) {

            _background.MilkyWayBrightness = galaxy;

        }

        var faintest = _background.FaintestMagnitude;

        if (ImGui.SliderFloat("Faintest magnitude"u8, ref faintest, 2.0f, 10.0f, "%.1f"u8)) {

            _background.FaintestMagnitude = faintest;

        }

    }

    private void DrawOcclusion() {

        ImGui.SeparatorText("Ambient occlusion"u8);

        var active = _occlusion.Active;

        if (ImGui.Checkbox("GTAO"u8, ref active)) {

            _occlusion.Active = active;

        }

        ImGui.SameLine();

        var show = _occlusion.ShowOcclusion;

        if (ImGui.Checkbox("Show occlusion"u8, ref show)) {

            _occlusion.ShowOcclusion = show;

        }

        var radius = _occlusion.Radius;

        if (ImGui.SliderFloat("Radius"u8, ref radius, 0.1f, 20.0f, "%.2f m"u8, ImGuiSliderFlags.Logarithmic)) {

            _occlusion.Radius = radius;

        }

        var falloff = _occlusion.Falloff;

        if (ImGui.SliderFloat("Falloff"u8, ref falloff, 0.05f, 1.0f, "%.2f"u8)) {

            _occlusion.Falloff = falloff;

        }

        var power = _occlusion.Power;

        if (ImGui.SliderFloat("Power"u8, ref power, 0.5f, 4.0f, "%.2f"u8)) {

            _occlusion.Power = power;

        }

        var pixels = _occlusion.MaxRadiusPixels;

        if (ImGui.SliderFloat("Max radius"u8, ref pixels, 8.0f, 256.0f, "%.0f px"u8)) {

            _occlusion.MaxRadiusPixels = pixels;

        }

    }

    private void DrawShadows() {

        ImGui.SeparatorText("Shadows"u8);

        var cast = _shadows.CastShadows;

        if (ImGui.Checkbox("Sun shadows"u8, ref cast)) {

            _shadows.CastShadows = cast;

        }

        ImGui.SameLine();

        var tint = _shadows.ShowCascades;

        if (ImGui.Checkbox("Tint cascades"u8, ref tint)) {

            _shadows.ShowCascades = tint;

        }

        var cascades = _shadows.Cascades;

        if (ImGui.SliderInt("Cascades"u8, ref cascades, 1, ShadowMaps.MaxCascades)) {

            _shadows.Cascades = cascades;

        }

        var distance = _shadows.MaxDistance;

        if (ImGui.SliderFloat("Distance"u8, ref distance, 50.0f, 50000.0f, "%.0f m"u8, ImGuiSliderFlags.Logarithmic)) {

            _shadows.MaxDistance = distance;

        }

        var lambda = _shadows.SplitLambda;

        if (ImGui.SliderFloat("Split lambda"u8, ref lambda, 0.0f, 1.0f, "%.2f"u8)) {

            _shadows.SplitLambda = lambda;

        }

        var offset = _shadows.NormalOffset;

        if (ImGui.SliderFloat("Normal offset"u8, ref offset, 0.0f, 5.0f, "%.2f texels"u8)) {

            _shadows.NormalOffset = offset;

        }

        var filter = _shadows.FilterRadius;

        if (ImGui.SliderFloat("Filter radius"u8, ref filter, 0.25f, 4.0f, "%.2f texels"u8)) {

            _shadows.FilterRadius = filter;

        }

        var constant = _shadows.ConstantBias;

        if (ImGui.SliderFloat("Constant bias"u8, ref constant, 0.0f, 10.0f, "%.2f"u8)) {

            _shadows.ConstantBias = constant;

        }

        var slope = _shadows.SlopeBias;

        if (ImGui.SliderFloat("Slope bias"u8, ref slope, 0.0f, 10.0f, "%.2f"u8)) {

            _shadows.SlopeBias = slope;

        }

        ImGui.Text($"Splits {_shadows.Split(0):F0} / {_shadows.Split(1):F0} / {_shadows.Split(2):F0} / {_shadows.Split(3):F0} m");

    }

    public void Draw() {

        ImGui.SetNextWindowPos(new float2(330, 330), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowCollapsed(true, ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Rendering"u8)) {

            ImGui.End();

            return;

        }

        ImGui.SeparatorText("Lighting"u8);
        ImGui.Text($"Sun ({_lighting.SunDirection.X:F2}, {_lighting.SunDirection.Y:F2}, {_lighting.SunDirection.Z:F2})  EV100 {_lighting.Ev100:F1}");
        ImGui.Text($"Sky {_lighting.SkyIrradiance.Y:F0} lx  ground {_lighting.GroundIrradiance.Y:F0} lx");

        DrawCamera();
        DrawShadows();
        DrawOcclusion();
        DrawBackground();

        ImGui.SeparatorText("Meshes"u8);
        ImGui.Text($"Models {_models.ModelCount}  draw calls {_meshes.DrawCount}");

        ImGui.SeparatorText("Terrain"u8);
        ImGui.Text($"Draws {_terrain.DrawCount}  resident {_terrain.ResidentChunks}  uploads {_terrain.Uploads}");
        ImGui.Text($"Built {_terrain.ChunksBuilt}  in flight {_terrain.JobsInFlight}");

        foreach (var body in _terrain.Bodies) {

            var quadtree = body.Quadtree;

            ImGui.Text($"{body.Body.Name}: {quadtree.Count} chunks, deepest {quadtree.DeepestLevel}/{quadtree.MaxLevel}, culled {quadtree.Culled}");

        }

        var split = _terrain.SplitDistance;

        if (ImGui.SliderFloat("Split distance"u8, ref split, 1.0f, 5.0f, "%.2f"u8)) {

            _terrain.SplitDistance = split;

        }

        var levels = _terrain.ShowLevels;

        if (ImGui.Checkbox("Tint LOD levels"u8, ref levels)) {

            _terrain.ShowLevels = levels;

        }

        ImGui.End();

    }

}
