namespace PartitionPilot;

public class DiskBarSegment
{
    public string Type { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Label { get; set; } = "";
    public string ColorHex { get; set; } = "#888888";
    public double Proportion { get; set; }
}
