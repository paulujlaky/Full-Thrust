using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace FullThrust.Game.Rendering;

// GPU time of each render pass and of the whole frame, from timestamp queries read back once their frame's fence has signalled.
public sealed class GpuProfiler : IDisposable {

    public const int MaxScopes = 32;

    private const int QueriesPerFrame = MaxScopes * 2 + 2;
    private const int FrameBegin = MaxScopes * 2;
    private const int FrameEnd = FrameBegin + 1;

    // Each query reads back as a 64-bit value followed by a 64-bit availability flag.
    private const int ResultStride = 16;

    private readonly DeviceEx _device;
    private readonly VkQueryPool _pool;
    private readonly double _nanosecondsPerTick;

    private readonly byte[] _results = new byte[QueriesPerFrame * ResultStride];

    // Queries are only readable once a frame has reset and written them.
    private readonly bool[] _recorded = new bool[Renderer.FramesInFlight];

    private readonly double[] _smoothed = new double[MaxScopes];
    private readonly double[] _sums = new double[MaxScopes];
    private readonly int[] _counts = new int[MaxScopes];

    private double _frameSum;
    private int _frameCount;

    public GpuProfiler(VulkanContext context) {

        _device = context.Device;
        _nanosecondsPerTick = context.PhysicalDevice.GetProperties().Limits.TimestampPeriod;

        _pool = _device.CreateQueryPool(new VkQueryPoolCreateInfo {

            QueryType = VkQueryType.Timestamp,
            QueryCount = QueriesPerFrame * Renderer.FramesInFlight,

        }, null);

    }

    public bool Enabled { get; set; } = true;

    // Exponentially smoothed milliseconds, for display.
    public double FrameMs { get; private set; }

    public double ScopeMs(int scope) => _smoothed[scope];

    // Means since the last ResetAverages, for logging.
    public double AverageFrameMs => _frameCount > 0 ? _frameSum / _frameCount : 0;

    public double AverageScopeMs(int scope) => _counts[scope] > 0 ? _sums[scope] / _counts[scope] : 0;

    public void ResetAverages() {

        Array.Clear(_sums);
        Array.Clear(_counts);

        _frameSum = 0;
        _frameCount = 0;

    }

    // Call after the frame's fence has been waited on and before its command buffer is recorded again.
    public void Collect(int frame) {

        if (!Enabled || !_recorded[frame]) {

            return;

        }

        _device.GetQueryPoolResults(_pool, frame * QueriesPerFrame, QueriesPerFrame, _results, new ByteSize64(ResultStride), VkQueryResultFlags._64Bit | VkQueryResultFlags.WithAvailabilityBit);

        if (TryTicks(FrameBegin, out var begin) && TryTicks(FrameEnd, out var end)) {

            var ms = Milliseconds(begin, end);

            FrameMs = Smooth(FrameMs, ms);
            _frameSum += ms;
            _frameCount++;

        }

        for (var scope = 0; scope < MaxScopes; scope++) {

            if (TryTicks(scope * 2, out var start) && TryTicks(scope * 2 + 1, out var stop)) {

                var ms = Milliseconds(start, stop);

                _smoothed[scope] = Smooth(_smoothed[scope], ms);
                _sums[scope] += ms;
                _counts[scope]++;

            }

        }

    }

    public void BeginFrame(CommandBuffer cmd, int frame) {

        if (!Enabled) {

            return;

        }

        cmd.ResetQueryPool(_pool, frame * QueriesPerFrame, QueriesPerFrame);
        _recorded[frame] = true;
        cmd.WriteTimestamp(VkPipelineStageFlags.BottomOfPipeBit, _pool, frame * QueriesPerFrame + FrameBegin);

    }

    public void EndFrame(CommandBuffer cmd, int frame) {

        if (Enabled) {

            cmd.WriteTimestamp(VkPipelineStageFlags.BottomOfPipeBit, _pool, frame * QueriesPerFrame + FrameEnd);

        }

    }

    public void Begin(CommandBuffer cmd, int frame, int scope) {

        if (Enabled && scope < MaxScopes) {

            cmd.WriteTimestamp(VkPipelineStageFlags.BottomOfPipeBit, _pool, frame * QueriesPerFrame + scope * 2);

        }

    }

    public void End(CommandBuffer cmd, int frame, int scope) {

        if (Enabled && scope < MaxScopes) {

            cmd.WriteTimestamp(VkPipelineStageFlags.BottomOfPipeBit, _pool, frame * QueriesPerFrame + scope * 2 + 1);

        }

    }

    private bool TryTicks(int query, out ulong ticks) {

        var offset = query * ResultStride;

        ticks = BitConverter.ToUInt64(_results, offset);

        return BitConverter.ToUInt64(_results, offset + 8) != 0;

    }

    private double Milliseconds(ulong start, ulong stop) => stop > start ? (stop - start) * _nanosecondsPerTick / 1e6 : 0;

    private static double Smooth(double current, double sample) => current == 0 ? sample : current + (sample - current) * 0.05;

    public void Dispose() => _device.DestroyQueryPool(_pool, null);

}
