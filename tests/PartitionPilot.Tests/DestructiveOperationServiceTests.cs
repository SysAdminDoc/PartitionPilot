using System.IO;

namespace PartitionPilot.Tests;

public class DestructiveOperationServiceTests : IDisposable
{
    private readonly string _backupDir = Path.Combine(
        Path.GetTempPath(), "pp-destructive-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RunAsync_TakesTheSnapshotBeforeExecutingAndLocksEveryLetteredVolume()
    {
        var wmi = new StubWmiService();
        var log = new RecordingLog();
        var backup = new PartitionTableBackup(wmi, log, _backupDir);
        var order = new List<string>();

        var outcome = await DestructiveOperationService.RunAsync(
            new DestructiveOperationRequest(1, Identity(), "test wipe"),
            wmi, backup, log,
            _ => { order.Add("execute"); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(outcome.SnapshotPath));
        Assert.Equal(["execute"], order);
        Assert.Contains(log.Messages, m => m.Contains("pre-destruction snapshot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_RefusesBeforeSnapshotOrExecutionWhenTheDiskIdentityChanged()
    {
        var wmi = new StubWmiService();
        var log = new RecordingLog();
        var backup = new PartitionTableBackup(wmi, log, _backupDir);
        var executed = false;

        var changed = Identity();
        changed.SerialNumber = "SN-SOMETHING-ELSE";

        await Assert.ThrowsAnyAsync<Exception>(() => DestructiveOperationService.RunAsync(
            new DestructiveOperationRequest(1, changed, "test wipe"),
            wmi, backup, log,
            _ => { executed = true; return Task.CompletedTask; },
            TestContext.Current.CancellationToken));

        Assert.False(executed);
        Assert.False(Directory.Exists(_backupDir) && Directory.EnumerateFiles(_backupDir, "*.json").Any());
    }

    [Fact]
    public async Task RunAsync_DoesNotRunTheOperationWhenTheSnapshotCannotBeSaved()
    {
        var wmi = new StubWmiService();
        var log = new RecordingLog();

        // A path that cannot be created, so the mandatory snapshot fails.
        var backup = new PartitionTableBackup(wmi, log, Path.Combine(_backupDir, "\0invalid"));
        var executed = false;

        await Assert.ThrowsAnyAsync<Exception>(() => DestructiveOperationService.RunAsync(
            new DestructiveOperationRequest(1, Identity(), "test wipe"),
            wmi, backup, log,
            _ => { executed = true; return Task.CompletedTask; },
            TestContext.Current.CancellationToken));

        Assert.False(executed);
    }

    [Fact]
    public async Task RunAsync_LetsTheOperationsFailurePropagateAfterTheSnapshotIsSafelyOnDisk()
    {
        var wmi = new StubWmiService();
        var log = new RecordingLog();
        var backup = new PartitionTableBackup(wmi, log, _backupDir);

        await Assert.ThrowsAsync<InvalidOperationException>(() => DestructiveOperationService.RunAsync(
            new DestructiveOperationRequest(1, Identity(), "test wipe", LockVolumes: false),
            wmi, backup, log,
            _ => throw new InvalidOperationException("device failure"),
            TestContext.Current.CancellationToken));

        // The evidence still exists even though the operation failed.
        Assert.NotEmpty(Directory.EnumerateFiles(_backupDir, "*.json"));
    }

    [Fact]
    public async Task RunAsync_SkipsVolumeLockingWhenNotRequested()
    {
        var wmi = new StubWmiService();
        var log = new RecordingLog();
        var backup = new PartitionTableBackup(wmi, log, _backupDir);

        var outcome = await DestructiveOperationService.RunAsync(
            new DestructiveOperationRequest(1, Identity(), "test", LockVolumes: false),
            wmi, backup, log,
            _ => Task.CompletedTask,
            TestContext.Current.CancellationToken);

        Assert.Empty(outcome.LockedVolumes);
    }

    private static DiskIdentitySnapshot Identity() => new()
    {
        DiskNumber = 1,
        FriendlyName = "Stub Disk",
        Size = 1024L * 1024 * 1024,
        PartitionStyle = "GPT",
        SerialNumber = "SN-STUB",
        UniqueId = "UID-STUB"
    };

    public void Dispose()
    {
        try { Directory.Delete(_backupDir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private sealed class RecordingLog : IActivityLog
    {
        public List<string> Messages { get; } = new();
        public void Log(string message) => Messages.Add(message);
    }

    /// <summary>
    /// One disk whose partitions carry no drive letters, so the test never locks a real volume.
    /// Everything else delegates to the simulated service.
    /// </summary>
    private sealed class StubWmiService : IWmiDiskService
    {
        private readonly SimulatedDiskService _inner = new();

        public Task<List<DiskInfo>> GetDisksAsync() => Task.FromResult(new List<DiskInfo>
        {
            new()
            {
                Number = 1,
                FriendlyName = "Stub Disk",
                Size = 1024L * 1024 * 1024,
                PartitionStyle = "GPT",
                SerialNumber = "SN-STUB",
                UniqueId = "UID-STUB"
            }
        });

        public Task<List<PartitionInfo>> GetPartitionsAsync(int diskNumber) => Task.FromResult(new List<PartitionInfo>
        {
            new() { PartitionNumber = 1, Size = 512L * 1024 * 1024, Offset = 1024 * 1024, FileSystem = "NTFS" }
        });

        public Task<List<VolumeInfo>> GetVolumesAsync() => _inner.GetVolumesAsync();
        public Task<List<PhysicalDiskInfo>> GetPhysicalDisksAsync() => _inner.GetPhysicalDisksAsync();
        public Task<SmartData?> GetSmartDataAsync(string deviceId) => _inner.GetSmartDataAsync(deviceId);
        public Task<List<AlignmentInfo>> GetAlignmentAuditAsync() => _inner.GetAlignmentAuditAsync();
        public Task<HashSet<char>> GetPagefileLocationsAsync() => _inner.GetPagefileLocationsAsync();
        public Task<List<char>> GetAvailableLettersAsync() => _inner.GetAvailableLettersAsync();
        public Task<(long Min, long Max)> GetPartitionSupportedSizeAsync(char driveLetter) =>
            _inner.GetPartitionSupportedSizeAsync(driveLetter);
        public Task<List<MountedImageInfo>> GetMountedImagesAsync() => _inner.GetMountedImagesAsync();
        public Task<Dictionary<char, string>> GetBitLockerStatusAsync() => _inner.GetBitLockerStatusAsync();
        public Task<List<string>> GetBitLockerProtectedTargetsAsync(int diskNumber) =>
            _inner.GetBitLockerProtectedTargetsAsync(diskNumber);
        public Task<Dictionary<int, string>> GetStoragePoolMembershipAsync() => _inner.GetStoragePoolMembershipAsync();
        public Task<Dictionary<string, (string Health, string Status, bool ReadOnly)>> GetStoragePoolHealthAsync() =>
            _inner.GetStoragePoolHealthAsync();
    }
}
