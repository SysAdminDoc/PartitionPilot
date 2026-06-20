namespace PartitionPilot;

public class PartitionLayoutSpec
{
    public string Style { get; set; } = "GPT";
    public List<PartitionSpec> Partitions { get; set; } = new();
}

public class PartitionSpec
{
    public string? SizeMB { get; set; }
    public bool UseMaximumSize { get; set; }
    public string FileSystem { get; set; } = "NTFS";
    public string Label { get; set; } = "";
    public string? DriveLetter { get; set; }
}
