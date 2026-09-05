namespace PartitionPilot.Tests;

public class SmartQueryServiceLifetimeTests
{
    [Fact]
    public void Shutdown_IsSafeWhenNothingWasEverOpened()
    {
        SmartQueryService.Shutdown();
        SmartQueryService.Shutdown();
    }

    [Fact]
    public void QueryDisk_SurvivesRepeatedCallsAndAShutdownInBetween()
    {
        // The shared instance is opened lazily and dropped on failure, so a query after a shutdown has to
        // be able to open a fresh one rather than failing for the rest of the process lifetime.
        var log = new RecordingLog();

        _ = SmartQueryService.QueryDisk(0, log);
        SmartQueryService.Shutdown();
        _ = SmartQueryService.QueryDisk(0, log);

        // No assertion on the payload: whether LibreHardwareMonitor reports this machine's disks depends
        // on elevation. What matters is that neither call threw and the second still ran.
        Assert.NotEmpty(log.Messages);
    }

    [Fact]
    public void QueryDisk_IsSafeFromSeveralThreadsAtOnce()
    {
        // The temperature monitor polls on a timer while the health tab can refresh, so two callers can
        // reach the shared instance at the same time.
        var log = new RecordingLog();

        Parallel.For(0, 8, _ => SmartQueryService.QueryDisk(0, log));

        SmartQueryService.Shutdown();
    }

    private sealed class RecordingLog : IActivityLog
    {
        private readonly Lock _gate = new();
        public List<string> Messages { get; } = new();

        public void Log(string message)
        {
            lock (_gate) Messages.Add(message);
        }
    }
}
