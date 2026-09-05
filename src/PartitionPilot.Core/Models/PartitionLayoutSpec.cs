namespace PartitionPilot;

public class PartitionLayoutSpec
{
    public string Style { get; set; } = "GPT";
    public DiskIdentitySnapshot? TargetDisk { get; set; }
    public List<PartitionSpec> Partitions { get; set; } = new();
}

public class PartitionSpec
{
    public string? SizeMB { get; set; }
    public bool UseMaximumSize { get; set; }
    public string FileSystem { get; set; } = "NTFS";
    public string Label { get; set; } = "";
    public string? DriveLetter { get; set; }

    /// <summary>
    /// Starting offset in kilobytes. Optional; when set, DiskPart is told exactly where to place the
    /// partition instead of packing it after the previous one. Snapshot restores set this so a
    /// recreated table lands on the offsets the original disk had.
    /// </summary>
    public long? OffsetKB { get; set; }
}
