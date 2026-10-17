using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

// Load-time uploads into device-local memory: record, submit and wait. Never used per frame.
public sealed class GpuUploader : IDisposable {

    private readonly VulkanContext _context;
    private readonly VkCommandPool _pool;
    private readonly CommandBuffer[] _cmd = new CommandBuffer[1];
    private readonly VkFence _fence;

    private readonly List<BufferEx> _staging = [];

    private bool _recording;

    public GpuUploader(VulkanContext context) {

        _context = context;

        var device = context.Device;

        _pool = device.CreateCommandPool(new VkCommandPoolCreateInfo {

            QueueFamilyIndex = context.GraphicsFamily,
            Flags = VkCommandPoolCreateFlags.ResetCommandBufferBit | VkCommandPoolCreateFlags.TransientBit,

        }, null);

        device.AllocateCommandBuffers(new VkCommandBufferAllocateInfo { CommandPool = _pool, Level = VkCommandBufferLevel.Primary }, _cmd);

        _fence = device.CreateFence(new VkFenceCreateInfo(), null);

    }

    public DeviceEx Device => _context.Device;

    public CommandBuffer Commands {

        get {

            if (!_recording) {

                _cmd[0].Reset(VkCommandBufferResetFlags.None);
                _cmd[0].Begin(new VkCommandBufferBeginInfo { Flags = VkCommandBufferUsageFlags.OneTimeSubmitBit });
                _recording = true;

            }

            return _cmd[0];

        }

    }

    // The returned buffer lives until Flush completes.
    public BufferEx Stage<T>(ReadOnlySpan<T> data) where T : unmanaged {

        var buffer = Device.CreateBuffer(new BufferEx.CreateInfo {

            Name = "Staging",
            BufferSize = ByteSize.Of<T>(Math.Max(1, data.Length)),
            BufferUsage = VkBufferUsageFlags.TransferSrcBit,
            AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,

        });

        using (var mapped = buffer.Map()) {

            data.CopyTo(mapped.AsSpan<T>());

        }

        _staging.Add(buffer);

        return buffer;

    }

    public BufferEx CreateDeviceBuffer<T>(string name, ReadOnlySpan<T> data, VkBufferUsageFlags usage) where T : unmanaged {

        var size = ByteSize.Of<T>(Math.Max(1, data.Length));

        var buffer = Device.CreateBuffer(new BufferEx.CreateInfo {

            Name = name,
            BufferSize = size,
            BufferUsage = usage | VkBufferUsageFlags.TransferDstBit,
            AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,

        });

        var staging = Stage(data);

        Commands.CopyBuffer(staging.VkBuffer, buffer.VkBuffer, [new VkBufferCopy { Size = size }]);

        return buffer;

    }

    public void Flush() {

        if (!_recording) {

            return;

        }

        _cmd[0].End();
        _recording = false;

        _context.GraphicsQueue.Submit([], [], _cmd, [], _fence);
        Device.WaitForFences([_fence], true, -1);
        Device.ResetFences([_fence]);

        foreach (var buffer in _staging) {

            buffer.Dispose();

        }

        _staging.Clear();

    }

    public void Dispose() {

        Flush();

        Device.DestroyFence(_fence, null);
        Device.DestroyCommandPool(_pool, null);

    }

}
