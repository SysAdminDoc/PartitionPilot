namespace PartitionPilot.Tests;

public class PartitionTableBackupTests
{
    [Fact]
    public void BuildDiff_ReportsNoChangesForMatchingLayout()
    {
        var snapshot = CreateSnapshot();
        var current = new[]
        {
            new PartitionInfo
            {
                PartitionNumber = 1,
                DriveLetter = 'C',
                Size = 1000,
                Offset = 1024,
                Type = "Basic"
            }
        };

        var diff = PartitionTableBackup.BuildDiff(snapshot, current);

        Assert.Contains("No partition-number", diff);
    }

    [Fact]
    public void BuildDiff_ReportsMissingChangedAndNewPartitions()
    {
        var snapshot = CreateSnapshot();
        snapshot.Partitions.Add(new PartitionSnapshotPartition
        {
            PartitionNumber = 2,
            DriveLetter = "D",
            Size = 2000,
            Offset = 4096,
            Type = "Recovery"
        });

        var current = new[]
        {
            new PartitionInfo
            {
                PartitionNumber = 1,
                DriveLetter = 'E',
                Size = 1500,
                Offset = 1024,
                Type = "Basic"
            },
            new PartitionInfo
            {
                PartitionNumber = 3,
                DriveLetter = 'F',
                Size = 3000,
                Offset = 8192,
                Type = "Basic"
            }
        };

        var diff = PartitionTableBackup.BuildDiff(snapshot, current);

        Assert.Contains("Changed: partition 1", diff);
        Assert.Contains("Missing now: partition 2", diff);
        Assert.Contains("New now: partition 3", diff);
    }

    [Fact]
    public void BuildRecoveryCommands_ProvidesNonDestructiveGuidance()
    {
        var snapshot = CreateSnapshot();

        var commands = PartitionTableBackup.BuildRecoveryCommands(snapshot);

        Assert.Contains("Get-Disk -Number 0", commands);
        Assert.Contains("Captured partition layout", commands);
        Assert.Contains("UniqueId=UID-0", commands);
        Assert.Contains("does not generate destructive restore commands", commands);
    }

    [Fact]
    public async Task SaveSnapshotForDestructiveOperationAsync_WritesSnapshotAndLogsPath()
    {
        var backupDir = Path.Combine(Path.GetTempPath(), "PartitionPilotTests", Guid.NewGuid().ToString("N"));
        var log = new TestLog();
        var backup = new PartitionTableBackup(new FakeWmiDiskService(), log, backupDir);

        try
        {
            var path = await backup.SaveSnapshotForDestructiveOperationAsync(
                2,
                "sector clone",
                TestContext.Current.CancellationToken);

            Assert.True(File.Exists(path));
            Assert.StartsWith(backupDir, path, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("sector_clone", Path.GetFileName(path));
            Assert.Contains("\"DiskNumber\": 2", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Contains(log.Messages, m =>
                m.Contains("Pre-destruction snapshot saved before sector clone", StringComparison.OrdinalIgnoreCase) &&
                m.Contains(path, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(backupDir))
                Directory.Delete(backupDir, recursive: true);
        }
    }

    [Fact]
    public async Task SaveSnapshotForDestructiveOperationAsync_LogsAndThrowsWhenSnapshotCannotBeWritten()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PartitionPilotTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var blockedPath = Path.Combine(tempRoot, "occupied");
        await File.WriteAllTextAsync(blockedPath, "not a directory", TestContext.Current.CancellationToken);

        var log = new TestLog();
        var backup = new PartitionTableBackup(new FakeWmiDiskService(), log, blockedPath);

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                backup.SaveSnapshotForDestructiveOperationAsync(
                    2,
                    "disk wipe",
                    TestContext.Current.CancellationToken));

            Assert.Contains(blockedPath, ex.Message);
            Assert.Contains("blocked before disk changes", ex.Message);
            Assert.Contains(log.Messages, m =>
                m.Contains("Could not save required pre-destruction partition snapshot", StringComparison.OrdinalIgnoreCase) &&
                m.Contains(blockedPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static PartitionSnapshot CreateSnapshot()
    {
        return new PartitionSnapshot
        {
            FilePath = @"C:\Temp\disk0_20260618_120000.json",
            Timestamp = "2026-06-18T12:00:00Z",
            DiskNumber = 0,
            DiskName = "Test Disk",
            DiskSize = 100_000,
            PartitionStyle = "GPT",
            DiskIdentity = new DiskIdentitySnapshot
            {
                DiskNumber = 0,
                FriendlyName = "Test Disk",
                Size = 100_000,
                PartitionStyle = "GPT",
                UniqueId = "UID-0",
                SerialNumber = "SER-0",
                Path = @"\\?\disk#0"
            },
            Partitions =
            {
                new PartitionSnapshotPartition
                {
                    PartitionNumber = 1,
                    DriveLetter = "C",
                    Size = 1000,
                    Offset = 1024,
                    Type = "Basic",
                    FileSystem = "NTFS"
                }
            }
        };
    }

    private sealed class TestLog : IActivityLog
    {
        public List<string> Messages { get; } = new();

        public void Log(string message) => Messages.Add(message);
    }

    private sealed class FakeWmiDiskService : IWmiDiskService
    {
        public Task<List<DiskInfo>> GetDisksAsync() => Task.FromResult(new List<DiskInfo>
        {
            new()
            {
                Number = 2,
                FriendlyName = "Target SSD",
                Size = 500_000,
                PartitionStyle = "GPT",
                UniqueId = "UID-2",
                SerialNumber = "SER-2",
                Path = @"\\?\disk#2",
                BusType = "NVMe"
            }
        });

        public Task<List<PartitionInfo>> GetPartitionsAsync(int diskNumber) => Task.FromResult(new List<PartitionInfo>
        {
            new()
            {
                DiskNumber = diskNumber,
                PartitionNumber = 1,
                DriveLetter = 'T',
                Label = "Target",
                Size = 100_000,
                Offset = 1_048_576,
                Type = "Basic",
                FileSystem = "NTFS"
            }
        });

        public Task<List<VolumeInfo>> GetVolumesAsync() => Task.FromResult(new List<VolumeInfo>());
        public Task<List<PhysicalDiskInfo>> GetPhysicalDisksAsync() => Task.FromResult(new List<PhysicalDiskInfo>());
        public Task<SmartData?> GetSmartDataAsync(string deviceId) => Task.FromResult<SmartData?>(null);
        public Task<List<AlignmentInfo>> GetAlignmentAuditAsync() => Task.FromResult(new List<AlignmentInfo>());
        public Task<HashSet<char>> GetPagefileLocationsAsync() => Task.FromResult(new HashSet<char>());
        public Task<List<char>> GetAvailableLettersAsync() => Task.FromResult(new List<char>());
        public Task<(long Min, long Max)> GetPartitionSupportedSizeAsync(char driveLetter) => Task.FromResult((0L, 0L));
        public Task<List<MountedImageInfo>> GetMountedImagesAsync() => Task.FromResult(new List<MountedImageInfo>());
        public Task<Dictionary<char, string>> GetBitLockerStatusAsync() => Task.FromResult(new Dictionary<char, string>());
        public Task<List<string>> GetBitLockerProtectedTargetsAsync(int diskNumber) => Task.FromResult(new List<string>());
        public Task<Dictionary<int, string>> GetStoragePoolMembershipAsync() => Task.FromResult(new Dictionary<int, string>());
        public Task<Dictionary<string, (string Health, string Status, bool ReadOnly)>> GetStoragePoolHealthAsync() =>
            Task.FromResult(new Dictionary<string, (string Health, string Status, bool ReadOnly)>());
    }
}
