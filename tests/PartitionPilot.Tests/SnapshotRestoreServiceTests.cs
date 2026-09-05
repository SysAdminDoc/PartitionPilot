namespace PartitionPilot.Tests;

public class SnapshotRestoreServiceTests
{
    private const long Mb = 1024L * 1024L;

    [Fact]
    public void ToLayoutSpec_PreservesOffsetsSizesAndFilesystemsInDiskOrder()
    {
        var spec = SnapshotRestoreService.ToLayoutSpec(Snapshot());

        Assert.Equal("GPT", spec.Style);
        Assert.Equal(3, spec.Partitions.Count);

        // Recorded out of order in the snapshot; the spec must follow the disk, not the file.
        Assert.Equal(["100", "16", "40960"], spec.Partitions.Select(p => p.SizeMB ?? "").ToArray());
        Assert.Equal([1024L, 103424L, 119808L], spec.Partitions.Select(p => p.OffsetKB!.Value).ToArray());
        Assert.Equal(["FAT32", "NTFS", "NTFS"], spec.Partitions.Select(p => p.FileSystem).ToArray());
        Assert.Equal(["", "", "C"], spec.Partitions.Select(p => p.DriveLetter ?? "").ToArray());
        Assert.Equal(["SYSTEM", "", "Windows"], spec.Partitions.Select(p => p.Label).ToArray());
        Assert.All(spec.Partitions, p => Assert.False(p.UseMaximumSize));
    }

    [Fact]
    public void ToLayoutSpec_CarriesTheSnapshotsDiskIdentityAsTheTarget()
    {
        var spec = SnapshotRestoreService.ToLayoutSpec(Snapshot());

        Assert.NotNull(spec.TargetDisk);
        Assert.Equal("SN-ALPHA", spec.TargetDisk!.SerialNumber);
        Assert.Equal(256L * 1024 * Mb, spec.TargetDisk.Size);
    }

    [Fact]
    public void ToLayoutSpec_RefusesToGuessAFilesystemForAPartitionThatRecordsNone()
    {
        // An unelevated capture routinely leaves FileSystem blank on the ESP. Defaulting it to NTFS
        // would recreate the ESP as NTFS and the machine would stop booting.
        var snapshot = Snapshot();
        snapshot.Partitions.Single(p => p.PartitionNumber == 1).FileSystem = "";

        var ex = Assert.Throws<ArgumentException>(() => SnapshotRestoreService.ToLayoutSpec(snapshot));

        Assert.Contains("does not record a filesystem", ex.Message);
        Assert.Contains("partition 1", ex.Message);
        Assert.Contains("unbootable", ex.Message);
    }

    [Fact]
    public void BuildPlan_RefusesBeforeProducingAnyDiskpartScriptWhenAFilesystemIsMissing()
    {
        var snapshot = Snapshot();
        snapshot.Partitions.Single(p => p.PartitionNumber == 1).FileSystem = "   ";

        Assert.Throws<ArgumentException>(() =>
            SnapshotRestoreService.BuildPlan(snapshot, Disk(), CurrentPartitions()));
    }

    [Fact]
    public void ToLayoutSpec_RejectsASnapshotWithNoPartitions()
    {
        var empty = new PartitionSnapshot { DiskNumber = 1, PartitionStyle = "GPT" };

        Assert.Throws<ArgumentException>(() => SnapshotRestoreService.ToLayoutSpec(empty));
    }

    [Fact]
    public void BuildPlan_ProducesDiskpartStepsThatRecreateEveryPartitionAtItsRecordedOffset()
    {
        var plan = SnapshotRestoreService.BuildPlan(Snapshot(), Disk(), CurrentPartitions());

        var creates = plan.Steps.Where(s => s.Action == "Create").ToList();
        Assert.Equal(3, creates.Count);
        Assert.Contains("create partition primary size=100 offset=1024", creates[0].DiskpartScript);
        Assert.Contains("create partition primary size=16 offset=103424", creates[1].DiskpartScript);
        Assert.Contains("create partition primary size=40960 offset=119808", creates[2].DiskpartScript);
        Assert.Contains("assign letter=C", creates[2].DiskpartScript);
    }

    [Fact]
    public void BuildPlan_ClearsTheDiskBeforeRecreatingIt()
    {
        var plan = SnapshotRestoreService.BuildPlan(Snapshot(), Disk(), CurrentPartitions());

        Assert.Equal("Clear", plan.Steps[0].Action);
        Assert.Equal("Destructive", plan.Steps[0].RiskLevel);
        Assert.Contains("clean", plan.Steps[0].DiskpartScript);
        Assert.Equal("Initialize", plan.Steps[1].Action);
    }

    [Fact]
    public void BuildPlan_StillClearsWhenTheCurrentLayoutAlreadyMatchesTheSnapshot()
    {
        // Every partition is recreated from the first index, so skipping the clean would issue
        // "create partition" against partitions that still exist.
        var snapshot = Snapshot();
        var matching = snapshot.Partitions
            .OrderBy(p => p.Offset)
            .Select(p => new PartitionInfo
            {
                PartitionNumber = p.PartitionNumber,
                Size = p.Size,
                Offset = p.Offset,
                FileSystem = p.FileSystem,
                Label = p.Label,
                DriveLetter = string.IsNullOrEmpty(p.DriveLetter) ? null : p.DriveLetter[0]
            })
            .ToList();

        var plan = SnapshotRestoreService.BuildPlan(snapshot, Disk(), matching);

        Assert.Equal("Clear", plan.Steps[0].Action);
        Assert.Equal(3, plan.Steps.Count(s => s.Action == "Create"));
    }

    [Fact]
    public void ToLayoutSpec_RoundsPartitionSizesUpSoNothingComesBackSmallerThanRecorded()
    {
        // DiskPart takes whole megabytes. Flooring would return a partition smaller than the data it
        // held, and anything under 1 MiB would floor to zero and be rejected, blocking the whole restore.
        var snapshot = Snapshot();
        snapshot.Partitions.Single(p => p.PartitionNumber == 2).Size = (16 * Mb) + 1;

        var spec = SnapshotRestoreService.ToLayoutSpec(snapshot);

        Assert.Equal("17", spec.Partitions[1].SizeMB);
    }

    [Fact]
    public void ToLayoutSpec_KeepsASubMegabytePartitionInsteadOfFlooringItToZero()
    {
        var snapshot = Snapshot();
        snapshot.Partitions.Single(p => p.PartitionNumber == 2).Size = 512 * 1024;

        var spec = SnapshotRestoreService.ToLayoutSpec(snapshot);

        Assert.Equal("1", spec.Partitions[1].SizeMB);
        LayoutDiffService.Validate(spec); // would throw on "0"
    }

    [Fact]
    public void ToLayoutSpec_BlocksAnOffsetItCannotReproduceExactly()
    {
        var snapshot = Snapshot();
        snapshot.Partitions.Single(p => p.PartitionNumber == 1).Offset = (1 * Mb) + 1;

        var ex = Assert.Throws<ArgumentException>(() => SnapshotRestoreService.ToLayoutSpec(snapshot));

        Assert.Contains("not a whole number of kilobytes", ex.Message);
    }

    [Fact]
    public void BuildPlan_WarnsThatBootAndSystemPartitionsComeBackEmpty()
    {
        var plan = SnapshotRestoreService.BuildPlan(Snapshot(), Disk(), CurrentPartitions());

        Assert.Contains(plan.SkippedPartitions, s => s.Contains("System", StringComparison.Ordinal));
        Assert.Contains(plan.SkippedPartitions, s => s.Contains("Boot", StringComparison.Ordinal));
        Assert.Contains("Not recreated by this plan", plan.FormatPlan());
    }

    [Fact]
    public void BuildPlan_FailsClosedWhenTheDiskIdentityDoesNotMatch()
    {
        var otherDisk = Disk();
        otherDisk.SerialNumber = "SN-BRAVO";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SnapshotRestoreService.BuildPlan(Snapshot(), otherDisk, CurrentPartitions()));

        Assert.Contains("captured from a different disk", ex.Message);
        Assert.Contains("Restore blocked", ex.Message);
    }

    [Fact]
    public void BuildPlan_FailsClosedWhenTheDiskSizeChanged()
    {
        var resized = Disk();
        resized.Size = 512L * 1024 * Mb;

        Assert.Throws<InvalidOperationException>(() =>
            SnapshotRestoreService.BuildPlan(Snapshot(), resized, CurrentPartitions()));
    }

    [Fact]
    public void AssertTargetMatches_AcceptsTheDiskTheSnapshotCameFrom()
    {
        SnapshotRestoreService.AssertTargetMatches(Snapshot(), Disk());
    }

    private static PartitionSnapshot Snapshot() => new()
    {
        FilePath = @"C:\ProgramData\PartitionPilot\snapshots\disk1.json",
        Timestamp = "2026-09-04T12:00:00+00:00",
        DiskNumber = 1,
        DiskName = "Contoso NVMe",
        DiskSize = 256L * 1024 * Mb,
        PartitionStyle = "GPT",
        DiskIdentity = new DiskIdentitySnapshot
        {
            DiskNumber = 1,
            FriendlyName = "Contoso NVMe",
            Size = 256L * 1024 * Mb,
            PartitionStyle = "GPT",
            SerialNumber = "SN-ALPHA",
            UniqueId = "UID-ALPHA"
        },
        Partitions =
        [
            // Deliberately unordered so the conversion has to sort by offset.
            new PartitionSnapshotPartition
            {
                PartitionNumber = 3, DriveLetter = "C", Label = "Windows", FileSystem = "NTFS",
                Size = 40960 * Mb, Offset = 117L * Mb, Type = "Basic", IsBoot = true
            },
            new PartitionSnapshotPartition
            {
                PartitionNumber = 1, Label = "SYSTEM", FileSystem = "FAT32",
                Size = 100 * Mb, Offset = 1 * Mb, Type = "System", IsSystem = true
            },
            new PartitionSnapshotPartition
            {
                PartitionNumber = 2, Label = "", FileSystem = "NTFS",
                Size = 16 * Mb, Offset = 101L * Mb, Type = "Reserved"
            }
        ]
    };

    private static DiskInfo Disk() => new()
    {
        Number = 1,
        FriendlyName = "Contoso NVMe",
        Size = 256L * 1024 * Mb,
        PartitionStyle = "GPT",
        SerialNumber = "SN-ALPHA",
        UniqueId = "UID-ALPHA"
    };

    private static List<PartitionInfo> CurrentPartitions() =>
    [
        new PartitionInfo { PartitionNumber = 1, Size = 200 * Mb, Offset = 1 * Mb, FileSystem = "NTFS" }
    ];
}
