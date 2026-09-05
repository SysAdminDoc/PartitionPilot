using System.IO;

namespace PartitionPilot.Tests;

public class SnapshotRetentionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pp-retention-" + Guid.NewGuid().ToString("N"));
    private readonly PartitionTableBackup _backup;
    private readonly RecordingLog _log = new();

    public SnapshotRetentionTests()
    {
        Directory.CreateDirectory(_dir);
        _backup = new PartitionTableBackup(new SimulatedDiskService(), _log, _dir);
    }

    [Fact]
    public async Task Purge_KeepsAPreDestructionSnapshotThatIsFarOlderThanTheAdHocRetentionWindow()
    {
        // The one an operator needs is old precisely because time has passed since the disk was wiped.
        var ancient = Write("disk0_20200101_000000000_dod_3_pass_wipe.json");

        await _backup.SaveSnapshotForDestructiveOperationAsync(0, "sector clone", TestContext.Current.CancellationToken);

        Assert.True(File.Exists(ancient));
    }

    [Fact]
    public async Task Purge_RemovesAnAdHocSnapshotOlderThanTheRetentionWindow()
    {
        var ancient = Write("disk0_20200101_000000000.json");
        var recent = Write($"disk0_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}.json");

        await _backup.SaveSnapshotForDestructiveOperationAsync(0, "sector clone", TestContext.Current.CancellationToken);

        Assert.False(File.Exists(ancient));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public async Task Purge_IgnoresTheFilesystemTimestampAndUsesTheNameStamp()
    {
        // A snapshot copied or restored from a backup gets a fresh creation time; the name is the truth.
        var ancient = Write("disk0_20200101_000000000.json");
        File.SetCreationTimeUtc(ancient, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(ancient, DateTime.UtcNow);

        await _backup.SaveSnapshotForDestructiveOperationAsync(0, "sector clone", TestContext.Current.CancellationToken);

        Assert.False(File.Exists(ancient));
    }

    [Fact]
    public async Task Purge_CapsPreDestructionSnapshotsPerDiskAndKeepsTheNewest()
    {
        for (var i = 0; i < 60; i++)
            Write($"disk0_202601{(i % 28) + 1:00}_{i:000}000000_wipe.json");

        await _backup.SaveSnapshotForDestructiveOperationAsync(0, "sector clone", TestContext.Current.CancellationToken);

        var remaining = Directory.GetFiles(_dir, "disk0_*_*.json")
            .Select(Path.GetFileName)
            .Where(n => n!.Count(c => c == '_') > 2)
            .ToList();

        Assert.Equal(50, remaining.Count);
        Assert.Contains(remaining, n => n!.Contains("_sector_clone", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Purge_CapsEachDiskIndependentlyRatherThanSharingOneBudget()
    {
        // 40 for disk 1 is under the cap and must survive in full, even while disk 2 is over it.
        for (var i = 0; i < 40; i++)
            Write($"disk1_202601{(i % 28) + 1:00}_{i:000}000000_wipe.json");
        for (var i = 0; i < 60; i++)
            Write($"disk2_202601{(i % 28) + 1:00}_{i:000}000000_wipe.json");

        await _backup.SaveSnapshotForDestructiveOperationAsync(0, "sector clone", TestContext.Current.CancellationToken);

        Assert.Equal(40, Directory.GetFiles(_dir, "disk1_*.json").Length);
        Assert.Equal(50, Directory.GetFiles(_dir, "disk2_*.json").Length);
    }

    private string Write(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "{\"DiskNumber\":0,\"Partitions\":[]}");
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private sealed class RecordingLog : IActivityLog
    {
        public List<string> Messages { get; } = new();
        public void Log(string message) => Messages.Add(message);
    }
}
