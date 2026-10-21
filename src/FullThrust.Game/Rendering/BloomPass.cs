using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

// Physically based bloom (Jimenez 2014): a 13-tap downsample chain, then tent upsamples summed back up; tonemapping mixes it in.
public sealed class BloomPass : IRenderPass, IResizeListener {

    private const int Levels = 6;
    private const int GroupSize = 8;

    private readonly DeviceEx _device;
    private readonly HdrTarget _target;

    private readonly RenderTexture[] _down = new RenderTexture[Levels];
    private readonly RenderTexture[] _up = new RenderTexture[Levels - 1];

    private readonly VkDescriptorSet[] _downSets = new VkDescriptorSet[Levels];
    private readonly VkDescriptorSet[] _upSets = new VkDescriptorSet[Levels - 1];

    private readonly VkSampler _sampler;
    private readonly VkDescriptorSetLayout _downLayout;
    private readonly VkDescriptorSetLayout _upLayout;
    private readonly VkDescriptorPool _pool;

    private readonly ComputeKernel _downsample;
    private readonly ComputeKernel _upsample;

    public BloomPass(DeviceEx device, ShaderLibrary shaders, HdrTarget target) {

        _device = device;
        _target = target;

        _sampler = device.CreateSampler(new VkSamplerCreateInfo {

            MagFilter = VkFilter.Linear,
            MinFilter = VkFilter.Linear,
            MipmapMode = VkSamplerMipmapMode.Nearest,
            AddressModeU = VkSamplerAddressMode.ClampToEdge,
            AddressModeV = VkSamplerAddressMode.ClampToEdge,
            AddressModeW = VkSamplerAddressMode.ClampToEdge,

        }, null);

        _downLayout = Descriptors.CreateLayout(device, [

            Descriptors.Binding(0, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.ComputeBit),
            Descriptors.Binding(1, VkDescriptorType.StorageImage, VkShaderStageFlags.ComputeBit),

        ]);

        _upLayout = Descriptors.CreateLayout(device, [

            Descriptors.Binding(0, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.ComputeBit),
            Descriptors.Binding(1, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.ComputeBit),
            Descriptors.Binding(2, VkDescriptorType.StorageImage, VkShaderStageFlags.ComputeBit),

        ]);

        _pool = Descriptors.CreatePool(device, Levels * 2, [

            new VkDescriptorPoolSize { Type = VkDescriptorType.CombinedImageSampler, DescriptorCount = Levels * 3 },
            new VkDescriptorPoolSize { Type = VkDescriptorType.StorageImage, DescriptorCount = Levels * 2 },

        ]);

        for (var i = 0; i < Levels; i++) {

            _down[i] = new RenderTexture(device, $"BloomDown{i}", VkFormat.R16G16B16A16SFloat, 1, 1);
            _downSets[i] = Descriptors.Allocate(device, _pool, _downLayout);

        }

        for (var i = 0; i < Levels - 1; i++) {

            _up[i] = new RenderTexture(device, $"BloomUp{i}", VkFormat.R16G16B16A16SFloat, 1, 1);
            _upSets[i] = Descriptors.Allocate(device, _pool, _upLayout);

        }

        _downsample = new ComputeKernel(device, shaders, "bloom_down.comp", _downLayout, ByteSize.Of<float4>());
        _upsample = new ComputeKernel(device, shaders, "bloom_up.comp", _upLayout, ByteSize.Of<float4>());

        OnResize(target.Extent);

    }

    public string Name => "Bloom";

    public RenderStage Stage => RenderStage.PostProcess;

    public bool Enabled { get; set; } = true;

    // Share of the final image that is bloom; a lens scatters a few percent of light this way.
    public float Intensity { get; set; } = 0.04f;

    // Weight of each wider octave against the one inside it; below 1 the glare falls off faster than 1/r^2, as a lens's does.
    public float Falloff { get; set; } = 0.6f;

    // Tent filter radius in texels of each lower level.
    public float Radius { get; set; } = 1.0f;

    public VkImageView View => _up[0].View;

    public VkSampler Sampler => _sampler;

    public void OnResize(VkExtent2D extent) {

        var width = extent.Width;
        var height = extent.Height;

        for (var i = 0; i < Levels; i++) {

            width = Math.Max(1, (width + 1) / 2);
            height = Math.Max(1, (height + 1) / 2);

            _down[i].Resize(width, height);

            if (i < Levels - 1) {

                _up[i].Resize(width, height);

            }

        }

        for (var i = 0; i < Levels; i++) {

            var source = i == 0 ? _target.ColorView : _down[i - 1].View;
            var layout = i == 0 ? VkImageLayout.ShaderReadOnlyOptimal : VkImageLayout.General;

            Descriptors.WriteImage(_device, _downSets[i], 0, source, _sampler, layout);
            Descriptors.WriteStorageImage(_device, _downSets[i], 1, _down[i].View);

        }

        for (var i = 0; i < Levels - 1; i++) {

            var lower = i == Levels - 2 ? _down[Levels - 1].View : _up[i + 1].View;

            Descriptors.WriteImage(_device, _upSets[i], 0, lower, _sampler, VkImageLayout.General);
            Descriptors.WriteImage(_device, _upSets[i], 1, _down[i].View, _sampler, VkImageLayout.General);
            Descriptors.WriteStorageImage(_device, _upSets[i], 2, _up[i].View);

        }

    }

    public void Record(in FrameContext frame) {

        var cmd = frame.CommandBuffer;

        foreach (var texture in _down) {

            texture.Prepare(cmd);

        }

        foreach (var texture in _up) {

            texture.Prepare(cmd);

        }

        Barriers.ShadersToCompute(cmd);

        for (var i = 0; i < Levels; i++) {

            _downsample.Dispatch(cmd, default, _downSets[i], new float4(i == 0 ? 1 : 0, 0, 0, 0), ComputeKernel.Groups(_down[i].Width, GroupSize), ComputeKernel.Groups(_down[i].Height, GroupSize), 1);
            Barriers.ComputeToShaders(cmd);

        }

        var weights = 0.0f;

        for (var i = 0; i < Levels; i++) {

            weights += MathF.Pow(Falloff, i);

        }

        for (var i = Levels - 2; i >= 0; i--) {

            var scale = i == 0 ? 1 / weights : 1;

            _upsample.Dispatch(cmd, default, _upSets[i], new float4(Radius, Falloff, scale, 0), ComputeKernel.Groups(_up[i].Width, GroupSize), ComputeKernel.Groups(_up[i].Height, GroupSize), 1);
            Barriers.ComputeToShaders(cmd);

        }

    }

    public void Dispose() {

        _downsample.Dispose();
        _upsample.Dispose();

        foreach (var texture in _down) {

            texture.Dispose();

        }

        foreach (var texture in _up) {

            texture.Dispose();

        }

        _device.DestroyDescriptorPool(_pool, null);
        _device.DestroyDescriptorSetLayout(_downLayout, null);
        _device.DestroyDescriptorSetLayout(_upLayout, null);
        _device.DestroySampler(_sampler, null);

    }

}
