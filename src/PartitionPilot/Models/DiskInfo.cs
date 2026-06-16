namespace PartitionPilot;

public class DiskInfo
{
    public int Number { get; set; }
    public string FriendlyName { get; set; } = "";
    public long Size { get; set; }
    public string PartitionStyle { get; set; } = ""; // "MBR", "GPT", "RAW"
    public long LargestFreeExtent { get; set; }
    public int NumberOfPartitions { get; set; }
    public string DisplayText => $"Disk {Number}: {FriendlyName}  ({SizeUtil.Format(Size)}, {PartitionStyle})";
}
