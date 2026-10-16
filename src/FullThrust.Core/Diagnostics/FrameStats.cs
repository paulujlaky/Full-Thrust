namespace FullThrust.Core.Diagnostics;

public sealed class FrameStats {

    private readonly float[] _frameTimesMs;

    private int _head;
    private int _filled;

    private long _lastAllocated;

    public FrameStats(int historyLength = 240) {

        _frameTimesMs = new float[historyLength];

    }

    public ReadOnlySpan<float> History => _frameTimesMs;

    public int HistoryOffset => _head;

    public float LastFrameMs { get; private set; }

    public float AverageMs { get; private set; }

    public float MinMs { get; private set; }

    public float MaxMs { get; private set; }

    public float Fps => AverageMs > 0 ? 1000.0f / AverageMs : 0;

    public long FrameCount { get; private set; }

    public long AllocatedBytesLastFrame { get; private set; }

    public long FramesWithAllocations { get; private set; }

    public void BeginFrame() => _lastAllocated = GC.GetAllocatedBytesForCurrentThread();

    public void EndFrame(double frameSeconds) {

        AllocatedBytesLastFrame = GC.GetAllocatedBytesForCurrentThread() - _lastAllocated;

        if (AllocatedBytesLastFrame > 0) {

            FramesWithAllocations++;

        }

        LastFrameMs = (float)(frameSeconds * 1000.0);

        _frameTimesMs[_head] = LastFrameMs;
        _head = (_head + 1) % _frameTimesMs.Length;
        _filled = Math.Min(_filled + 1, _frameTimesMs.Length);

        FrameCount++;

        var sum = 0.0f;
        var min = float.MaxValue;
        var max = 0.0f;

        for (var i = 0; i < _filled; i++) {

            var ms = _frameTimesMs[i];

            sum += ms;
            min = Math.Min(min, ms);
            max = Math.Max(max, ms);

        }

        AverageMs = sum / _filled;
        MinMs = min;
        MaxMs = max;

    }

    public void ResetAllocationCounter() => FramesWithAllocations = 0;

}
