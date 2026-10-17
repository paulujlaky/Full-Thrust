using FullThrust.Core.Terrain;

namespace FullThrust.Game.Terrain;

// A chunk build in flight. The main thread fills the request, a worker fills the result.
public sealed class TerrainJob {

    public readonly TerrainVertex[] Vertices = new TerrainVertex[TerrainChunkBuilder.VertexCount];

    public ulong Key;
    public int Body;
    public TerrainNode Node;

    public ITerrain? Terrain;
    public TerrainPalette? Palette;
    public double Radius;

    public TerrainBounds Bounds;

}

// Builds chunks on dedicated threads with a fixed pool of jobs, so neither side allocates per chunk.
public sealed class TerrainWorkers : IDisposable {

    private readonly TerrainJob[] _jobs;
    private readonly RingQueue _free;
    private readonly RingQueue _pending;
    private readonly RingQueue _done;

    private readonly SemaphoreSlim _signal = new(0);
    private readonly Thread[] _threads;

    private volatile bool _stopping;

    private long _built;

    public TerrainWorkers(int jobCount, int threadCount) {

        _jobs = new TerrainJob[jobCount];
        _free = new RingQueue(jobCount);
        _pending = new RingQueue(jobCount);
        _done = new RingQueue(jobCount);

        for (var i = 0; i < jobCount; i++) {

            _jobs[i] = new TerrainJob();
            _free.Enqueue(i);

        }

        _threads = new Thread[threadCount];

        for (var i = 0; i < threadCount; i++) {

            _threads[i] = new Thread(Work) { IsBackground = true, Name = $"Terrain worker {i}", Priority = ThreadPriority.BelowNormal };
            _threads[i].Start();

        }

    }

    public int InFlight => _jobs.Length - _free.Count;

    public long Built => Interlocked.Read(ref _built);

    public TerrainJob Job(int index) => _jobs[index];

    public bool TryRent(out int job) => _free.TryDequeue(out job);

    public void Submit(int job) {

        _pending.Enqueue(job);
        _signal.Release();

    }

    public bool TryTakeCompleted(out int job) => _done.TryDequeue(out job);

    public void Return(int job) {

        _jobs[job].Terrain = null;
        _jobs[job].Palette = null;
        _free.Enqueue(job);

    }

    private void Work() {

        var builder = new TerrainChunkBuilder();

        while (true) {

            _signal.Wait();

            if (_stopping) {

                return;

            }

            if (!_pending.TryDequeue(out var index)) {

                continue;

            }

            var job = _jobs[index];

            job.Bounds = builder.Build(job.Terrain!, job.Palette!, job.Radius, job.Node, job.Vertices);

            Interlocked.Increment(ref _built);
            _done.Enqueue(index);

        }

    }

    public void Dispose() {

        _stopping = true;
        _signal.Release(_threads.Length);

        foreach (var thread in _threads) {

            thread.Join();

        }

        _signal.Dispose();

    }

    // Fixed-capacity FIFO of job indices guarded by a lock.
    private sealed class RingQueue {

        private readonly int[] _items;
        private readonly Lock _lock = new();

        private int _head;
        private int _count;

        public RingQueue(int capacity) {

            _items = new int[capacity];

        }

        public int Count {

            get {

                lock (_lock) {

                    return _count;

                }

            }

        }

        public void Enqueue(int item) {

            lock (_lock) {

                _items[(_head + _count) % _items.Length] = item;
                _count++;

            }

        }

        public bool TryDequeue(out int item) {

            lock (_lock) {

                if (_count == 0) {

                    item = -1;

                    return false;

                }

                item = _items[_head];
                _head = (_head + 1) % _items.Length;
                _count--;

                return true;

            }

        }

    }

}
