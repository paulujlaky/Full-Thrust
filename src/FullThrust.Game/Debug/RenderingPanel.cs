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

    public RenderingPanel(TerrainRenderer terrain, MeshPass meshes, ModelLibrary models, SceneLighting lighting) {

        _terrain = terrain;
        _meshes = meshes;
        _models = models;
        _lighting = lighting;

    }

    public string Title => "Rendering";

    public bool Visible { get; set; } = true;

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
