namespace PartitionPilot;

public class SmartReading
{
    public DateTimeOffset Timestamp { get; set; }
    public int? Temperature { get; set; }
    public int? Wear { get; set; }
    public long? ReallocatedSectors { get; set; }
    public long? PendingSectors { get; set; }
    public long? PowerOnHours { get; set; }
    public long? TotalBytesWritten { get; set; }
    public int? NvmeAvailableSpare { get; set; }
    public long? NvmeMediaErrors { get; set; }

    public static SmartReading FromSmartData(SmartData data) => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        Temperature = data.Temperature,
        Wear = data.Wear,
        ReallocatedSectors = data.ReallocatedSectors,
        PendingSectors = data.PendingSectors,
        PowerOnHours = data.PowerOnHours,
        TotalBytesWritten = data.TotalBytesWritten,
        NvmeAvailableSpare = data.NvmeAvailableSpare,
        NvmeMediaErrors = data.NvmeMediaErrors
    };
}

public class SmartTrend
{
    public string Attribute { get; set; } = "";
    public string Direction { get; set; } = "Stable";
    public string Severity { get; set; } = "Info";
    public string Message { get; set; } = "";
}
