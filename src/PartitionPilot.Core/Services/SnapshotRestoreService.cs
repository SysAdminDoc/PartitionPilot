namespace PartitionPilot;

public sealed record SnapshotRestorePlan(
    PartitionLayoutSpec Spec,
    List<LayoutDiffEntry> Steps,
    IReadOnlyList<string> SkippedPartitions)
{
    public string FormatPlan()
    {
        var plan = LayoutDiffService.FormatPlan(Steps);
        if (SkippedPartitions.Count == 0)
            return plan;

        var skipped = string.Join("\n", SkippedPartitions.Select(s => $"  - {s}"));
        return $"{plan}\nNot recreated by this plan:\n{skipped}\n";
    }
}

/// <summary>
/// Turns a captured partition snapshot back into an applicable layout.
/// <para>
/// Snapshots were previously evidence only: recovery produced a command list the operator had to
/// retype. This converts one into the same <see cref="PartitionLayoutSpec"/> the layout planner
/// already validates and applies, so the mandatory pre-destruction snapshot becomes a real rollback.
/// </para>
/// </summary>
public static class SnapshotRestoreService
{
    /// <summary>
    /// Partitions whose contents DiskPart cannot recreate from a layout description. Their geometry is
    /// still reproduced, but the operator is told plainly that the data is not coming back.
    /// </summary>
    private static readonly string[] ContentBearingRoles = ["Boot", "System", "Recovery"];

    public static PartitionLayoutSpec ToLayoutSpec(PartitionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Partitions.Count == 0)
            throw new ArgumentException("Snapshot records no partitions, so there is no layout to restore.", nameof(snapshot));

        AssertEveryPartitionRecordsAFilesystem(snapshot);

        var style = string.IsNullOrWhiteSpace(snapshot.PartitionStyle) ? "GPT" : snapshot.PartitionStyle.Trim().ToUpperInvariant();

        return new PartitionLayoutSpec
        {
            Style = style,
            TargetDisk = snapshot.EffectiveDiskIdentity,
            Partitions = snapshot.Partitions
                .OrderBy(p => p.Offset)
                .Select(ToPartitionSpec)
                .ToList()
        };
    }

    /// <summary>
    /// Builds the dry-run plan for restoring <paramref name="snapshot"/> onto <paramref name="disk"/>.
    /// Restoring a table always clears the disk first, so the destructive path is taken deliberately
    /// rather than left to the caller.
    /// </summary>
    public static SnapshotRestorePlan BuildPlan(
        PartitionSnapshot snapshot,
        DiskInfo disk,
        List<PartitionInfo> currentPartitions)
    {
        ArgumentNullException.ThrowIfNull(disk);

        var spec = ToLayoutSpec(snapshot);
        AssertTargetMatches(snapshot, disk);

        var steps = LayoutDiffService.ComputeDiff(spec, disk, currentPartitions, allowDestructiveReplace: true);
        return new SnapshotRestorePlan(spec, steps, DescribeUnrecoverableContent(snapshot));
    }

    /// <summary>
    /// Fail-closed identity check. Runs before any DiskPart script is produced, so a snapshot taken from
    /// one disk can never be replayed onto another.
    /// </summary>
    public static void AssertTargetMatches(PartitionSnapshot snapshot, DiskInfo disk)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(disk);

        if (!snapshot.EffectiveDiskIdentity.Matches(disk, out var mismatch))
            throw new InvalidOperationException(
                $"Snapshot was captured from a different disk than Disk {disk.Number}: {mismatch}. " +
                "Restore blocked before any partition table change.");
    }

    /// <summary>
    /// Refuses a snapshot that does not say what each partition was formatted with.
    /// <para>
    /// Guessing here is not a cosmetic default. An EFI System Partition recorded with a blank
    /// filesystem would be recreated as NTFS and the machine would stop booting. A snapshot captured
    /// without elevation routinely reports blank filesystems, so this is the common case, not an edge one.
    /// </para>
    /// </summary>
    private static void AssertEveryPartitionRecordsAFilesystem(PartitionSnapshot snapshot)
    {
        var missing = snapshot.Partitions
            .Where(p => string.IsNullOrWhiteSpace(p.FileSystem))
            .Select(p => $"partition {p.PartitionNumber} ({p.SizeText}, {p.Type})")
            .ToList();

        if (missing.Count == 0)
            return;

        throw new ArgumentException(
            $"Snapshot does not record a filesystem for {string.Join(", ", missing)}. " +
            "Recreating those partitions would have to guess a format, and guessing NTFS for an EFI System " +
            "Partition leaves the disk unbootable. Recapture the snapshot from an elevated session and retry.");
    }

    private const long BytesPerMebibyte = 1024L * 1024L;

    private static PartitionSpec ToPartitionSpec(PartitionSnapshotPartition partition)
    {
        if (partition.Size <= 0)
            throw new ArgumentException($"Snapshot partition {partition.PartitionNumber} records a non-positive size.");
        if (partition.Offset <= 0)
            throw new ArgumentException($"Snapshot partition {partition.PartitionNumber} records a non-positive offset.");

        // DiskPart takes whole megabytes. Rounding up rather than down keeps a partition from coming back
        // smaller than the data it held; a partition under 1 MiB would otherwise floor to 0 and be
        // rejected outright, taking the whole restore with it. The explicit offsets keep the layout
        // aligned regardless, so rounding the size up cannot walk the partitions forward.
        var sizeMb = (partition.Size + BytesPerMebibyte - 1) / BytesPerMebibyte;

        if (partition.Offset % 1024 != 0)
            throw new ArgumentException(
                $"Snapshot partition {partition.PartitionNumber} starts at byte offset {partition.Offset}, " +
                "which is not a whole number of kilobytes. DiskPart cannot reproduce that offset exactly, " +
                "so the restore is blocked rather than silently moving the partition.");

        return new PartitionSpec
        {
            SizeMB = sizeMb.ToString(System.Globalization.CultureInfo.InvariantCulture),
            UseMaximumSize = false,
            FileSystem = partition.FileSystem.Trim(),
            Label = partition.Label ?? "",
            DriveLetter = string.IsNullOrWhiteSpace(partition.DriveLetter) ? null : partition.DriveLetter,
            OffsetKB = partition.Offset / 1024L
        };
    }

    private static List<string> DescribeUnrecoverableContent(PartitionSnapshot snapshot)
    {
        var warnings = new List<string>();

        foreach (var partition in snapshot.Partitions.OrderBy(p => p.Offset))
        {
            var roles = new List<string>();
            if (partition.IsBoot) roles.Add("Boot");
            if (partition.IsSystem) roles.Add("System");
            if (partition.Type.Contains("Recovery", StringComparison.OrdinalIgnoreCase)) roles.Add("Recovery");

            var matched = roles.Where(r => ContentBearingRoles.Contains(r, StringComparer.Ordinal)).ToList();
            if (matched.Count > 0)
                warnings.Add(
                    $"Partition {partition.PartitionNumber} ({string.Join("/", matched)}, {partition.SizeText}) is recreated empty — " +
                    "its contents are not restored by a layout replay. Repair boot files afterwards or restore from an image.");
        }

        return warnings;
    }
}
