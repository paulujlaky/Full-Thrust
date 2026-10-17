using System.Text;
using System.Text.RegularExpressions;
using Brutal.ShaderCApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

public sealed partial class ShaderLibrary : IDisposable {

    private readonly Device _device;

    private readonly Compiler _compiler;
    private readonly CompileOptions _options;

    private readonly string _root;

    public ShaderLibrary(Device device, string root) {

        _device = device;
        _root = root;

        _compiler = ShaderC.CreateCompiler();
        _options = ShaderC.CreateCompilerOptions();

        _options.SetTargetEnv(TargetEnv.Vulkan, (uint)EnvVersion.Vulkan_1_3);
        _options.SetOptimizationLevel(OptimizationLevel.Performance);

    }

    public unsafe VkShaderModule Load(string fileName) {

        var path = Path.Combine(_root, fileName);
        var source = Preprocess(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var kind = ShaderC.InferShaderKind(path);

        using var result = _compiler.Compile(source, kind, fileName, "main", _options);

        var spirv = new ReadOnlySpan<byte>(result.GetBytes(), (int)result.GetLength());

        return ShaderModuleExtensions.FromSpirv(_device, spirv, null);

    }

    // Resolves #include "file" relative to the including file; each file is pasted at most once.
    private static string Preprocess(string path, HashSet<string> included) {

        if (!included.Add(Path.GetFullPath(path))) {

            return "";

        }

        var directory = Path.GetDirectoryName(path) ?? "";
        var output = new StringBuilder();

        foreach (var line in File.ReadAllLines(path)) {

            var match = IncludePattern().Match(line);

            if (match.Success) {

                output.AppendLine(Preprocess(Path.Combine(directory, match.Groups[1].Value), included));

            } else {

                output.AppendLine(line);

            }

        }

        return output.ToString();

    }

    [GeneratedRegex("^\\s*#include\\s+\"([^\"]+)\"")]
    private static partial Regex IncludePattern();

    public void Dispose() {

        _options.Dispose();
        _compiler.Dispose();

    }

}
