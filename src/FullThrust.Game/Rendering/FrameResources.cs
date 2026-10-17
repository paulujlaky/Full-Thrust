using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

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

}

// One uniform buffer per frame in flight, bound as descriptor set 0 by every scene pipeline.
public sealed class FrameResources : IDisposable {

    private readonly DeviceEx _device;

    private readonly VkDescriptorPool _pool;

    private readonly BufferEx[] _buffers = new BufferEx[Renderer.FramesInFlight];
    private readonly MappedMemory[] _mapped = new MappedMemory[Renderer.FramesInFlight];
    private readonly VkDescriptorSet[] _sets = new VkDescriptorSet[Renderer.FramesInFlight];

    public FrameResources(DeviceEx device) {

        _device = device;

        SetLayout = Descriptors.CreateLayout(device, [

            Descriptors.Binding(0, VkDescriptorType.UniformBuffer, VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit),

        ]);

        _pool = Descriptors.CreatePool(device, Renderer.FramesInFlight, [

            new VkDescriptorPoolSize { Type = VkDescriptorType.UniformBuffer, DescriptorCount = Renderer.FramesInFlight },

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

            Descriptors.WriteUniformBuffer(device, _sets[i], 0, _buffers[i].VkBuffer, ByteSize.Of<FrameUniforms>());

        }

    }

    public VkDescriptorSetLayout SetLayout { get; }

    public VkDescriptorSet Set(int frameIndex) => _sets[frameIndex];

    public void Write(int frameIndex, in FrameUniforms uniforms) => _mapped[frameIndex].AsSpan<FrameUniforms>()[0] = uniforms;

    public void Dispose() {

        for (var i = 0; i < _buffers.Length; i++) {

            _mapped[i].Dispose();
            _buffers[i].Dispose();

        }

        _device.DestroyDescriptorPool(_pool, null);
        _device.DestroyDescriptorSetLayout(SetLayout, null);

    }

}
