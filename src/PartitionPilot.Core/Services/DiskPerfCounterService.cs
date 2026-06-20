using System.Diagnostics;

namespace PartitionPilot;

public class DiskPerfSnapshot
{
    public int DiskNumber { get; set; }
    public double ReadMBps { get; set; }
    public double WriteMBps { get; set; }
    public double ReadIOPS { get; set; }
    public double WriteIOPS { get; set; }
    public double AvgReadLatencyMs { get; set; }
    public double AvgWriteLatencyMs { get; set; }
    public double QueueLength { get; set; }
    public double IdlePercent { get; set; }
}

public class DiskPerfCounterService : IDisposable
{
    private readonly Dictionary<int, DiskCounterSet> _counters = new();
    private Timer? _timer;
    private bool _disposed;

    public event Action<List<DiskPerfSnapshot>>? Updated;

    public void Start(IEnumerable<int> diskNumbers, int intervalMs = 2000)
    {
        Stop();
        foreach (var num in diskNumbers)
        {
            try
            {
                var instanceName = FindInstanceName(num);
                if (instanceName is null) continue;
                _counters[num] = new DiskCounterSet(instanceName);
            }
            catch { }
        }

        foreach (var cs in _counters.Values) cs.PrimeCounters();
        _timer = new Timer(_ => Poll(), null, intervalMs, intervalMs);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        foreach (var cs in _counters.Values) cs.Dispose();
        _counters.Clear();
    }

    private void Poll()
    {
        var snapshots = new List<DiskPerfSnapshot>();
        foreach (var (num, cs) in _counters)
        {
            try
            {
                snapshots.Add(new DiskPerfSnapshot
                {
                    DiskNumber = num,
                    ReadMBps = cs.DiskReadBytesPerSec.NextValue() / (1024 * 1024),
                    WriteMBps = cs.DiskWriteBytesPerSec.NextValue() / (1024 * 1024),
                    ReadIOPS = cs.DiskReadsPerSec.NextValue(),
                    WriteIOPS = cs.DiskWritesPerSec.NextValue(),
                    AvgReadLatencyMs = cs.AvgDiskSecPerRead.NextValue() * 1000,
                    AvgWriteLatencyMs = cs.AvgDiskSecPerWrite.NextValue() * 1000,
                    QueueLength = cs.AvgDiskQueueLength.NextValue(),
                    IdlePercent = cs.PercentIdleTime.NextValue()
                });
            }
            catch { }
        }
        Updated?.Invoke(snapshots);
    }

    private static string? FindInstanceName(int diskNumber)
    {
        try
        {
            var cat = new PerformanceCounterCategory("PhysicalDisk");
            foreach (var instance in cat.GetInstanceNames())
            {
                if (instance.StartsWith($"{diskNumber} ", StringComparison.Ordinal) ||
                    instance == diskNumber.ToString())
                    return instance;
            }
        }
        catch { }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    private sealed class DiskCounterSet : IDisposable
    {
        public PerformanceCounter DiskReadBytesPerSec { get; }
        public PerformanceCounter DiskWriteBytesPerSec { get; }
        public PerformanceCounter DiskReadsPerSec { get; }
        public PerformanceCounter DiskWritesPerSec { get; }
        public PerformanceCounter AvgDiskSecPerRead { get; }
        public PerformanceCounter AvgDiskSecPerWrite { get; }
        public PerformanceCounter AvgDiskQueueLength { get; }
        public PerformanceCounter PercentIdleTime { get; }

        public DiskCounterSet(string instance)
        {
            DiskReadBytesPerSec = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", instance);
            DiskWriteBytesPerSec = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", instance);
            DiskReadsPerSec = new PerformanceCounter("PhysicalDisk", "Disk Reads/sec", instance);
            DiskWritesPerSec = new PerformanceCounter("PhysicalDisk", "Disk Writes/sec", instance);
            AvgDiskSecPerRead = new PerformanceCounter("PhysicalDisk", "Avg. Disk sec/Read", instance);
            AvgDiskSecPerWrite = new PerformanceCounter("PhysicalDisk", "Avg. Disk sec/Write", instance);
            AvgDiskQueueLength = new PerformanceCounter("PhysicalDisk", "Avg. Disk Queue Length", instance);
            PercentIdleTime = new PerformanceCounter("PhysicalDisk", "% Idle Time", instance);
        }

        public void PrimeCounters()
        {
            try { DiskReadBytesPerSec.NextValue(); } catch { }
            try { DiskWriteBytesPerSec.NextValue(); } catch { }
            try { DiskReadsPerSec.NextValue(); } catch { }
            try { DiskWritesPerSec.NextValue(); } catch { }
            try { AvgDiskSecPerRead.NextValue(); } catch { }
            try { AvgDiskSecPerWrite.NextValue(); } catch { }
            try { AvgDiskQueueLength.NextValue(); } catch { }
            try { PercentIdleTime.NextValue(); } catch { }
        }

        public void Dispose()
        {
            DiskReadBytesPerSec.Dispose();
            DiskWriteBytesPerSec.Dispose();
            DiskReadsPerSec.Dispose();
            DiskWritesPerSec.Dispose();
            AvgDiskSecPerRead.Dispose();
            AvgDiskSecPerWrite.Dispose();
            AvgDiskQueueLength.Dispose();
            PercentIdleTime.Dispose();
        }
    }
}
