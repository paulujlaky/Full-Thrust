using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

[InlineArray(ShadowMaps.MaxCascades)]
public struct CascadeMatrices {

    private float4x4 _element;

}

// Mirrors the Frame block in Shaders/common.glsl (std140, so everything is a vec4 or mat4).
[StructLayout(LayoutKind.Sequential)]
public struct FrameUniforms {

    public float4x4 View;
    public float4x4 Projection;
    public float4x4 ViewProjection;
    public float4x4 InverseViewProjection;

    public float4 CameraPosition;

    // w is the cosine of the sun's angular radius.
    public float4 SunDirection;
    public float4 SunIlluminance;

    public float4 AmbientUp;
    public float4 SkyIrradiance;
    public float4 GroundIrradiance;

    // x exposure, y EV100, z tonemapper, w seconds.
    public float4 Exposure;

    public CascadeMatrices ShadowViewProjection;

    // Far view distance of each cascade.
    public float4 ShadowSplits;

    // World size of one shadow texel in each cascade.
    public float4 ShadowTexel;

    // x cascade count (0 disables), y normal offset in texels, z filter radius in texels, w tints cascades.
    public float4 ShadowParams;

    // x: 1 when the ambient occlusion image holds this frame's result.
    public float4 AmbientOcclusion;

}

// Descriptor set 0, shared by every scene pipeline: frame uniforms plus the textures any shader may sample.
public static class GlobalBindings {

    public const int Frame = 0;
    public const int ShadowMap = 1;
    public const int AmbientOcclusion = 2;

    // Every binding after Frame is a combined image sampler; add new ones here and in common.glsl.
    public const int Count = 3;

}

// One uniform buffer per frame in flight, bound as descriptor set 0 by every scene pipeline.
public sealed class FrameResources : IDisposable {

    private const VkShaderStageFlags AllStages = VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit | VkShaderStageFlags.ComputeBit;

    private readonly DeviceEx _device;

    private readonly VkDescriptorPool _pool;

    private readonly BufferEx[] _buffers = new BufferEx[Renderer.FramesInFlight];
    private readonly MappedMemory[] _mapped = new MappedMemory[Renderer.FramesInFlight];
    private readonly VkDescriptorSet[] _sets = new VkDescriptorSet[Renderer.FramesInFlight];

    public FrameResources(DeviceEx device) {

        _device = device;

        Span<VkDescriptorSetLayoutBinding> bindings = stackalloc VkDescriptorSetLayoutBinding[GlobalBindings.Count];

        bindings[0] = Descriptors.Binding(GlobalBindings.Frame, VkDescriptorType.UniformBuffer, AllStages);

        for (var i = 1; i < GlobalBindings.Count; i++) {

            bindings[i] = Descriptors.Binding(i, VkDescriptorType.CombinedImageSampler, AllStages);

        }

        SetLayout = Descriptors.CreateLayout(device, bindings);

        _pool = Descriptors.CreatePool(device, Renderer.FramesInFlight, [

            new VkDescriptorPoolSize { Type = VkDescriptorType.UniformBuffer, DescriptorCount = Renderer.FramesInFlight },
            new VkDescriptorPoolSize { Type = VkDescriptorType.CombinedImageSampler, DescriptorCount = Renderer.FramesInFlight * (GlobalBindings.Count - 1) },

        ]);

        for (var i = 0; i < Renderer.FramesInFlight; i++) {

            _buffers[i] = device.CreateBuffer(new BufferEx.CreateInfo {

                Name = "FrameUniforms",
                BufferSize = ByteSize.Of<FrameUniforms>(),
                BufferUsage = VkBufferUsageFlags.UniformBufferBit,
                AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,

            });

            _mapped[i] = _buffers[i].Map();
            _sets[i] = Descriptors.Allocate(device, _pool, SetLayout);

            Descriptors.WriteUniformBuffer(device, _sets[i], GlobalBindings.Frame, _buffers[i].VkBuffer, ByteSize.Of<FrameUniforms>());

        }

    }

    public VkDescriptorSetLayout SetLayout { get; }

    public VkDescriptorSet Set(int frameIndex) => _sets[frameIndex];

    public void Write(int frameIndex, in FrameUniforms uniforms) => _mapped[frameIndex].AsSpan<FrameUniforms>()[0] = uniforms;

    // Not per frame: call at load or on resize, while no frame that uses the sets is in flight.
    public void WriteImage(int binding, VkImageView view, VkSampler sampler, VkImageLayout layout = VkImageLayout.ShaderReadOnlyOptimal) {

        foreach (var set in _sets) {

            Descriptors.WriteImage(_device, set, binding, view, sampler, layout);

        }

    }

    public void Dispose() {

        for (var i = 0; i < _buffers.Length; i++) {

            _mapped[i].Dispose();
            _buffers[i].Dispose();

        }

        _device.DestroyDescriptorPool(_pool, null);
        _device.DestroyDescriptorSetLayout(SetLayout, null);

    }

}
