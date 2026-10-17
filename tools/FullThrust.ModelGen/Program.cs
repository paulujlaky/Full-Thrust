using System.Text.Json;
using FullThrust.Assets;
using FullThrust.ModelGen;

// dotnet run --project tools/FullThrust.ModelGen -- [build [recipes.json]] | inspect <model.glb>
var repository = FindRepository();
var modelsRoot = Path.Combine(repository, "data", "models");

if (args.Length >= 2 && args[0] == "inspect") {

    Inspect(args[1]);

    return;

}

var recipesPath = args.Length >= 2 && args[0] == "build" ? args[1] : Path.Combine(modelsRoot, "recipes.json");

var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
var recipes = JsonSerializer.Deserialize<RecipeFile>(File.ReadAllText(recipesPath), options) ?? throw new InvalidDataException("Recipe file is empty.");
var parts = LoadPartDimensions(Path.Combine(repository, "data", "parts", "parts.json"));

foreach (var recipe in recipes.Models) {

    if (recipe.Part is { } id) {

        if (!parts.TryGetValue(id, out var dimensions)) {

            throw new InvalidDataException($"{recipe.Output}: unknown part '{id}'.");

        }

        recipe.Length = recipe.Length > 0 ? recipe.Length : dimensions.Length;
        recipe.Diameter = recipe.Diameter > 0 ? recipe.Diameter : dimensions.Diameter;

    }

    var model = Generators.Build(recipe, modelsRoot);
    var output = Path.Combine(modelsRoot, recipe.Output);

    GltfModelWriter.WriteGlb(model, output, "FullThrust.ModelGen");

    Console.WriteLine($"{recipe.Output}: {model.TriangleCount} triangles, {model.Textures.Length} textures, {new FileInfo(output).Length / 1024} KiB, bounds {model.BoundsMin} .. {model.BoundsMax}");

}

static void Inspect(string path) {

    var model = GltfModelLoader.Load(path);

    Console.WriteLine($"{model.Name}: {model.TriangleCount} triangles, bounds {model.BoundsMin} .. {model.BoundsMax}");

    for (var i = 0; i < model.Instances.Length; i++) {

        var instance = model.Instances[i];
        var mesh = model.Meshes[instance.Mesh];
        var single = new ModelData { Vertices = model.Vertices, Indices = model.Indices, Primitives = model.Primitives, Meshes = model.Meshes, Instances = [instance] };

        single.ComputeBounds();

        var materials = string.Join(",", Enumerable.Range(mesh.FirstPrimitive, mesh.PrimitiveCount).Select(p => model.Materials[model.Primitives[p].Material].Name));

        Console.WriteLine($"  [{i}] {mesh.Name} {single.BoundsMin} .. {single.BoundsMax} {materials}");

    }

}

static Dictionary<string, (double Length, double Diameter)> LoadPartDimensions(string path) {

    using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

    var result = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase);

    foreach (var part in document.RootElement.GetProperty("parts"u8).EnumerateArray()) {

        result[part.GetProperty("id"u8).GetString()!] = (part.GetProperty("length"u8).GetDouble(), part.GetProperty("diameter"u8).GetDouble());

    }

    return result;

}

static string FindRepository() {

    for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent) {

        if (File.Exists(Path.Combine(directory.FullName, "FullThrust.slnx"))) {

            return directory.FullName;

        }

    }

    throw new InvalidOperationException("Run ModelGen from inside the Full-Thrust repository.");

}
