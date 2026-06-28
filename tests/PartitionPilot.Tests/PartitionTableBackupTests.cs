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
}
