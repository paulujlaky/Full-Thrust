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

    public RenderingPanel(TerrainRenderer terrain, MeshPass meshes, ModelLibrary models, SceneLighting lighting, ShadowMaps shadows) {

        _terrain = terrain;
        _meshes = meshes;
        _models = models;
        _lighting = lighting;
        _shadows = shadows;

    }

    public string Title => "Rendering";

    public bool Visible { get; set; } = true;

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

        DrawShadows();

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
