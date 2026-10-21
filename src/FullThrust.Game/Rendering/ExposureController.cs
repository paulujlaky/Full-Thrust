using System.Diagnostics;
using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

// Meters the HDR image on the GPU and adapts the camera's EV100 towards it, like the eye or an auto-exposing camera.
public sealed class ExposureController : IRenderPass, IResizeListener {

    private const int GroupPixels = 64;
    private const int MaxGroups = 4096;

    // Reflected-light meter calibration (ISO 2720).
    private const double MeterConstant = 12.5;

    private readonly DeviceEx _device;
    private readonly HdrTarget _target;
    private readonly SceneLighting _lighting;

    private readonly BufferEx[] _buffers = new BufferEx[Renderer.FramesInFlight];
    private readonly MappedMemory[] _mapped = new MappedMemory[Renderer.FramesInFlight];
    private readonly VkDescriptorSet[] _sets = new VkDescriptorSet[Renderer.FramesInFlight];

    // How many groups each frame in flight wrote, to read its sums when they come back.
    private readonly int[] _groups = new int[Renderer.FramesInFlight];

    private readonly VkSampler _sampler;
    private readonly VkDescriptorSetLayout _setLayout;
    private readonly VkDescriptorPool _pool;
    private readonly ComputeKernel _kernel;

    private long _last;

    public ExposureController(DeviceEx device, ShaderLibrary shaders, HdrTarget target, SceneLighting lighting) {

        _device = device;
        _target = target;
        _lighting = lighting;

        _sampler = device.CreateSampler(new VkSamplerCreateInfo {

            MagFilter = VkFilter.Nearest,
            MinFilter = VkFilter.Nearest,
            MipmapMode = VkSamplerMipmapMode.Nearest,
            AddressModeU = VkSamplerAddressMode.ClampToEdge,
            AddressModeV = VkSamplerAddressMode.ClampToEdge,
            AddressModeW = VkSamplerAddressMode.ClampToEdge,

        }, null);

        _setLayout = Descriptors.CreateLayout(device, [

            Descriptors.Binding(0, VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.ComputeBit),
            Descriptors.Binding(1, VkDescriptorType.StorageBuffer, VkShaderStageFlags.ComputeBit),

        ]);

        _pool = Descriptors.CreatePool(device, Renderer.FramesInFlight, [

            new VkDescriptorPoolSize { Type = VkDescriptorType.CombinedImageSampler, DescriptorCount = Renderer.FramesInFlight },
            new VkDescriptorPoolSize { Type = VkDescriptorType.StorageBuffer, DescriptorCount = Renderer.FramesInFlight },

        ]);

        var size = ByteSize.Of<float2>(MaxGroups);

        for (var i = 0; i < Renderer.FramesInFlight; i++) {

            _buffers[i] = device.CreateBuffer(new BufferEx.CreateInfo {

                Name = "LuminanceSums",
                BufferSize = size,
                BufferUsage = VkBufferUsageFlags.StorageBufferBit,
                AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,

            });

            _mapped[i] = _buffers[i].Map();
            _sets[i] = Descriptors.Allocate(device, _pool, _setLayout);

            Descriptors.WriteStorageBuffer(device, _sets[i], 1, _buffers[i].VkBuffer, size);

        }

        _kernel = new ComputeKernel(device, shaders, "luminance.comp", _setLayout, ByteSize.Of<float4>());

        OnResize(target.Extent);

    }

    public string Name => "Auto exposure";

    public RenderStage Stage => RenderStage.PostProcess;

    public bool Enabled { get; set; } = true;

    // Off pins SceneLighting.Ev100 where it is.
    public bool Automatic { get; set; } = true;

    // Positive brightens the image, in stops.
    public float Compensation { get; set; }

    // Seconds for the exposure to close most of the gap to its target.
    public float AdaptationTime { get; set; } = 1.2f;

    // Eyes and cameras stop adapting well before night looks like day.
    public float MinimumEv { get; set; } = -1.0f;

    public float MaximumEv { get; set; } = 18.0f;

    // Average scene luminance last measured, in cd/m2.
    public float MeasuredLuminance { get; private set; }

    public void OnResize(VkExtent2D extent) {

        foreach (var set in _sets) {

            Descriptors.WriteImage(_device, set, 0, _target.ColorView, _sampler);

        }

    }

    public void Record(in FrameContext frame) {

        var index = frame.FrameIndex;

        // The exposure this frame's image was rendered with, before adapting changes it.
        var exposure = _lighting.Exposure;

        // This frame's fence has been waited on, so the sums written the last time this slot was used are ready.
        Adapt(index);

        var groupsX = ComputeKernel.Groups(frame.Extent.Width, GroupPixels);
        var groupsY = ComputeKernel.Groups(frame.Extent.Height, GroupPixels);

        _groups[index] = Math.Min(groupsX * groupsY, MaxGroups);

        if (groupsX * groupsY > MaxGroups) {

            return;

        }

        var cmd = frame.CommandBuffer;

        _kernel.Dispatch(cmd, default, _sets[index], new float4(frame.Extent.Width, frame.Extent.Height, 1.0f / exposure, 0), groupsX, groupsY, 1);

    }

    private void Adapt(int index) {

        var now = Stopwatch.GetTimestamp();
        var seconds = _last == 0 ? 0 : Stopwatch.GetElapsedTime(_last, now).TotalSeconds;

        _last = now;

        if (_groups[index] == 0) {

            return;

        }

        var sums = MemoryMarshal.Cast<byte, float2>(_mapped[index].AsSpan<byte>())[.._groups[index]];
        var total = 0.0;
        var weight = 0.0;

        foreach (var sum in sums) {

            total += sum.X;
            weight += sum.Y;

        }

        if (weight <= 0) {

            return;

        }

        var luminance = Math.Pow(2, total / weight);

        MeasuredLuminance = (float)luminance;

        if (!Automatic) {

            return;

        }

        var target = Math.Clamp(Math.Log2(luminance * 100 / MeterConstant) - Compensation, MinimumEv, MaximumEv);
        var blend = 1 - Math.Exp(-Math.Min(seconds, 0.25) / Math.Max(AdaptationTime, 0.01));

        _lighting.Ev100 = (float)(_lighting.Ev100 + (target - _lighting.Ev100) * blend);

    }

    public void Dispose() {

        _kernel.Dispose();

        for (var i = 0; i < _buffers.Length; i++) {

            _mapped[i].Dispose();
            _buffers[i].Dispose();

        }

        _device.DestroyDescriptorPool(_pool, null);
        _device.DestroyDescriptorSetLayout(_setLayout, null);
        _device.DestroySampler(_sampler, null);

    }

}
