using Brutal.ShaderCApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

public sealed class ShaderLibrary : IDisposable {

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
        var source = File.ReadAllText(path);
        var kind = ShaderC.InferShaderKind(path);

        using var result = _compiler.Compile(source, kind, fileName, "main", _options);

        var spirv = new ReadOnlySpan<byte>(result.GetBytes(), (int)result.GetLength());

        return ShaderModuleExtensions.FromSpirv(_device, spirv, null);

    }

    public void Dispose() {

        _options.Dispose();
        _compiler.Dispose();

    }

}
