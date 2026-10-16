using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

public sealed class GpuMesh : IDisposable {

    private readonly BufferEx _vertices;
    private readonly BufferEx _indices;

    public GpuMesh(DeviceEx device, string name, MeshData mesh) {

        // Host-visible is fine for static debug meshes; real assets will go through a staging upload.
        _vertices = device.CreateBuffer(new BufferEx.CreateInfo {

            Name = name + ".Vertices",
            BufferSize = ByteSize.Of<Vertex>(mesh.Vertices.Length),
            BufferUsage = VkBufferUsageFlags.VertexBufferBit,
            AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,

        });

        _indices = device.CreateBuffer(new BufferEx.CreateInfo {

            Name = name + ".Indices",
            BufferSize = ByteSize.Of<uint>(mesh.Indices.Length),
            BufferUsage = VkBufferUsageFlags.IndexBufferBit,
            AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,

        });

        using (var mapped = _vertices.Map()) {

            mesh.Vertices.AsSpan().CopyTo(mapped.AsSpan<Vertex>());

        }

        using (var mapped = _indices.Map()) {

            mesh.Indices.AsSpan().CopyTo(mapped.AsSpan<uint>());

        }

        IndexCount = mesh.Indices.Length;

    }

    public int IndexCount { get; }

    public void Bind(CommandBuffer cmd) {

        cmd.BindVertexBuffer(0, _vertices.VkBuffer);
        cmd.BindIndexBuffer(_indices.VkBuffer, ByteSize.Zero, VkIndexType.UInt32);

    }

    public void Dispose() {

        _vertices.Dispose();
        _indices.Dispose();

    }

}
