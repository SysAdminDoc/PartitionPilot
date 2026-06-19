namespace PartitionPilot;

public class DiskInfo
{
    public int Number { get; set; }
    public string FriendlyName { get; set; } = "";
    public long Size { get; set; }
    public string PartitionStyle { get; set; } = ""; // "MBR", "GPT", "RAW"
    public long LargestFreeExtent { get; set; }
    public int NumberOfPartitions { get; set; }
    public string StoragePoolName { get; set; } = "";
    public bool IsPooled => !string.IsNullOrEmpty(StoragePoolName);
    public bool IsRaw => PartitionStyle.Equals("RAW", StringComparison.OrdinalIgnoreCase);
    public string DisplayText => IsPooled
        ? $"Disk {Number}: {FriendlyName}  ({SizeUtil.Format(Size)}, {PartitionStyle}, Pool: {StoragePoolName})"
        : $"Disk {Number}: {FriendlyName}  ({SizeUtil.Format(Size)}, {PartitionStyle})";
}
