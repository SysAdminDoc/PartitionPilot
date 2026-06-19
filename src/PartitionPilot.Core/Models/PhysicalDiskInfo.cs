namespace PartitionPilot;

public class PhysicalDiskInfo
{
    public string DeviceId { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string FirmwareVersion { get; set; } = "";
    public string MediaType { get; set; } = "Unknown"; // "HDD", "SSD", "Unknown"
    public string BusType { get; set; } = "";           // "NVMe", "SATA", "USB", etc.
    public long Size { get; set; }
    public int LogicalSectorSize { get; set; }
    public int PhysicalSectorSize { get; set; }
    public string HealthStatus { get; set; } = "";
    public string OperationalStatus { get; set; } = "";

    public string DisplayText =>
        $"{FriendlyName} ({SizeUtil.Format(Size)}, {MediaType}, {BusType}) — {HealthStatus}";
}
