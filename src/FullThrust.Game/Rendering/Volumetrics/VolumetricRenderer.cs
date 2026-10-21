using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering.Volumetrics;

// Ray-marches every volume source at half resolution after the opaque scene; Composite blends the result back in.
public sealed class VolumetricRenderer : IRenderPass, IResizeListener, IFrameUniformsWriter {

    public const int Capacity = 64;

    private const int GroupSize = 8;
    private const int HeaderBytes = 16;

    private static readonly int PrimitiveBytes = Marshal.SizeOf<VolumePrimitive>();

    private readonly DeviceEx _device;
    private readonly HdrTarget _target;

    private readonly VolumeNoise _noise;
    private readonly VolumeList _volumes = new(Capacity);
    private readonly List<IVolumeSource> _sources = [];

    private readonly BufferEx[] _buffers = new BufferEx[Renderer.FramesInFlight];
    private readonly MappedMemory[] _mapped = new MappedMemory[Renderer.FramesInFlight];
    private readonly VkDescriptorSet[] _sets = new VkDescriptorSet[Renderer.FramesInFlight];

    private readonly VkSampler _pointSampler;
    private readonly VkDescriptorSetLayout _setLayout;
    private readonly VkDescriptorPool _pool;
    private readonly ComputeKernel _march;

    private readonly RenderTexture _color;
    private readonly RenderTexture _depth;

    public VolumetricRenderer(DeviceEx device, ShaderLibrary shaders, HdrTarget target, FrameResources frames) {

        _device = device;
        _target = target;

        _noise = new VolumeNoise(device, shaders, frames.SetLayout);

        _pointSampler = device.CreateSampler(new VkSamplerCreateInfo {

            MagFilter = VkFilter.Nearest,
            MinFilter = VkFilter.Nearest,
            MipmapMode = VkSamplerMipmapMode.Nearest,
            AddressModeU = VkSamplerAddressMode.ClampToEdge,
            AddressModeV = VkSamplerAddressMode.ClampToEdge,
            AddressModeW = VkSamplerAddressMode.ClampToEdge,

        }, null);

        _setLayout = Descriptors.CreateLayout(device, [

            Descriptors.Binding(0, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.ComputeBit),
            Descriptors.Binding(1, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.ComputeBit),
            Descriptors.Binding(2, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.ComputeBit),
            Descriptors.Binding(3, VkDescriptorType.StorageBuffer, VkShaderStageFlags.ComputeBit),
            Descriptors.Binding(4, VkDescriptorType.StorageImage, VkShaderStageFlags.ComputeBit),
            Descriptors.Binding(5, VkDescriptorType.StorageImage, VkShaderStageFlags.ComputeBit),

        ]);

        _pool = Descriptors.CreatePool(device, Renderer.FramesInFlight, [

            new VkDescriptorPoolSize { Type = VkDescriptorType.CombinedImageSampler, DescriptorCount = 3 * Renderer.FramesInFlight },
            new VkDescriptorPoolSize { Type = VkDescriptorType.StorageBuffer, DescriptorCount = Renderer.FramesInFlight },
            new VkDescriptorPoolSize { Type = VkDescriptorType.StorageImage, DescriptorCount = 2 * Renderer.FramesInFlight },

        ]);

        var size = new ByteSize((nuint)(HeaderBytes + Capacity * PrimitiveBytes));

        for (var i = 0; i < Renderer.FramesInFlight; i++) {

            _buffers[i] = device.CreateBuffer(new BufferEx.CreateInfo {

                Name = "Volumes",
                BufferSize = size,
                BufferUsage = VkBufferUsageFlags.StorageBufferBit,
                AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,

            });

            _mapped[i] = _buffers[i].Map();
            _sets[i] = Descriptors.Allocate(device, _pool, _setLayout);

            Descriptors.WriteStorageBuffer(device, _sets[i], 3, _buffers[i].VkBuffer, size);

        }

        _march = new ComputeKernel(device, shaders, "volumetrics.comp", frames.SetLayout, _setLayout, ByteSize.Zero);

        var half = Half(target.Extent);

        _color = new RenderTexture(device, "VolumeColor", VkFormat.R16G16B16A16SFloat, half.Width, half.Height);
        _depth = new RenderTexture(device, "VolumeDepth", VkFormat.R32SFloat, half.Width, half.Height);

        WriteDescriptors();

        CompositePass = new Composite(this, shaders, frames.SetLayout);

        frames.WriteImage(GlobalBindings.CloudNoise, _noise.Shape, _noise.Sampler, VkImageLayout.General);

    }

    public string Name => "Volumetrics";

    public RenderStage Stage => RenderStage.AfterOpaque;

    public bool Enabled { get; set; } = true;

    public int VolumeCount => _volumes.Count;

    // Register with the renderer after this pass.
    public IRenderPass CompositePass { get; }

    public void AddSource(IVolumeSource source) => _sources.Add(source);

    public void Fill(ref FrameUniforms uniforms, bool enabled) {

        foreach (var source in _sources) {

            if (source is IFrameUniformsWriter writer) {

                writer.Fill(ref uniforms, enabled);

            }

        }

    }

    private static VkExtent2D Half(VkExtent2D extent) => new() { Width = Math.Max(1, (extent.Width + 1) / 2), Height = Math.Max(1, (extent.Height + 1) / 2) };

    public void OnResize(VkExtent2D extent) {

        var half = Half(extent);

        _color.Resize(half.Width, half.Height);
        _depth.Resize(half.Width, half.Height);

        WriteDescriptors();
        ((Composite)CompositePass).WriteDescriptors();

    }

    private void WriteDescriptors() {

        foreach (var set in _sets) {

            Descriptors.WriteImage(_device, set, 0, _target.DepthView, _pointSampler, VkImageLayout.DepthStencilReadOnlyOptimal);
            Descriptors.WriteImage(_device, set, 1, _noise.Shape, _noise.Sampler, VkImageLayout.General);
            Descriptors.WriteImage(_device, set, 2, _noise.Detail, _noise.Sampler, VkImageLayout.General);
            Descriptors.WriteStorageImage(_device, set, 4, _color.View);
            Descriptors.WriteStorageImage(_device, set, 5, _depth.View);

        }

    }

    public void Record(in FrameContext frame) {

        var cmd = frame.CommandBuffer;

        _noise.Prepare(cmd, frame.FrameSet);
        _color.Prepare(cmd);
        _depth.Prepare(cmd);

        _volumes.Clear();

        foreach (var source in _sources) {

            if (source.Visible) {

                source.Collect(_volumes);

            }

        }

        var bytes = _mapped[frame.FrameIndex].AsSpan<byte>();

        MemoryMarshal.Write(bytes, new int4(_volumes.Count, 0, 0, 0));
        _volumes.Items.CopyTo(MemoryMarshal.Cast<byte, VolumePrimitive>(bytes[HeaderBytes..]));

        if (_volumes.Count == 0) {

            return;

        }

        Barriers.ShadersToCompute(cmd);
        _march.Dispatch(cmd, frame.FrameSet, _sets[frame.FrameIndex], ComputeKernel.Groups(_color.Width, GroupSize), ComputeKernel.Groups(_color.Height, GroupSize), 1);
        Barriers.ComputeToShaders(cmd);

    }

    // The renderer disposes CompositePass with its other passes.
    public void Dispose() {

        _march.Dispose();
        _noise.Dispose();
        _color.Dispose();
        _depth.Dispose();

        for (var i = 0; i < _buffers.Length; i++) {

            _mapped[i].Dispose();
            _buffers[i].Dispose();

        }

        _device.DestroyDescriptorPool(_pool, null);
        _device.DestroyDescriptorSetLayout(_setLayout, null);
        _device.DestroySampler(_pointSampler, null);

    }

    // Upsamples the half-resolution volumes with depth-aware weights and blends them over the scene.
    private sealed class Composite : IRenderPass {

        private readonly VolumetricRenderer _owner;

        private readonly VkDescriptorSetLayout _setLayout;
        private readonly VkDescriptorPool _pool;
        private readonly VkDescriptorSet _set;
        private readonly VkPipelineLayout _layout;
        private readonly VkPipeline _pipeline;

        public unsafe Composite(VolumetricRenderer owner, ShaderLibrary shaders, VkDescriptorSetLayout frameLayout) {

            _owner = owner;

            var device = owner._device;

            _setLayout = Descriptors.CreateLayout(device, [

                Descriptors.Binding(0, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.FragmentBit),
                Descriptors.Binding(1, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.FragmentBit),
                Descriptors.Binding(2, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.FragmentBit),

            ]);

            _pool = Descriptors.CreatePool(device, 1, [new VkDescriptorPoolSize { Type = VkDescriptorType.CombinedImageSampler, DescriptorCount = 3 }]);
            _set = Descriptors.Allocate(device, _pool, _setLayout);
            _layout = device.CreatePipelineLayout([frameLayout, _setLayout], [], null);

            // Premultiplied: destination times the volume's transmittance, plus its light.
            var attachment = new VkPipelineColorBlendAttachmentState {

                BlendEnable = true,
                SrcColorBlendFactor = VkBlendFactor.One,
                DstColorBlendFactor = VkBlendFactor.SrcAlpha,
                ColorBlendOp = VkBlendOp.Add,
                SrcAlphaBlendFactor = VkBlendFactor.Zero,
                DstAlphaBlendFactor = VkBlendFactor.One,
                AlphaBlendOp = VkBlendOp.Add,
                ColorWriteMask = VkColorComponentFlags.RBit | VkColorComponentFlags.GBit | VkColorComponentFlags.BBit | VkColorComponentFlags.ABit,

            };

            var blend = new VkPipelineColorBlendStateCreateInfo { AttachmentCount = 1, Attachments = &attachment };

            _pipeline = Pipelines.Create(device, shaders, new PipelineDesc {

                RenderPass = owner._target.TransparentPass,
                Layout = _layout,
                VertexShader = "fullscreen.vert",
                FragmentShader = "volume_composite.frag",
                Cull = VkCullModeFlags.None,
                Depth = DepthMode.None,
                Blend = &blend,

            });

            WriteDescriptors();

        }

        public string Name => "Volumetric composite";

        public RenderStage Stage => RenderStage.Transparent;

        public bool Enabled { get; set; } = true;

        public void WriteDescriptors() {

            var device = _owner._device;

            Descriptors.WriteImage(device, _set, 0, _owner._color.View, _owner._pointSampler, VkImageLayout.General);
            Descriptors.WriteImage(device, _set, 1, _owner._depth.View, _owner._pointSampler, VkImageLayout.General);
            Descriptors.WriteImage(device, _set, 2, _owner._target.DepthView, _owner._pointSampler, VkImageLayout.DepthStencilReadOnlyOptimal);

        }

        public void Record(in FrameContext frame) {

            if (!_owner.Enabled || _owner._volumes.Count == 0) {

                return;

            }

            var cmd = frame.CommandBuffer;

            cmd.BindPipeline(VkPipelineBindPoint.Graphics, _pipeline);
            cmd.BindDescriptorSet(VkPipelineBindPoint.Graphics, _layout, 0, frame.FrameSet, []);
            cmd.BindDescriptorSet(VkPipelineBindPoint.Graphics, _layout, 1, _set, []);
            cmd.Draw(3, 1, 0, 0);

        }

        public void Dispose() {

            var device = _owner._device;

            device.DestroyPipeline(_pipeline, null);
            device.DestroyPipelineLayout(_layout, null);
            device.DestroyDescriptorPool(_pool, null);
            device.DestroyDescriptorSetLayout(_setLayout, null);

        }

    }

}
