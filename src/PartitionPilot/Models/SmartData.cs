namespace PartitionPilot;

public class SmartData
{
    public int? Temperature { get; set; }
    public int? Wear { get; set; }
    public long? PowerOnHours { get; set; }
    public long? ReadErrorsTotal { get; set; }
    public long? ReadErrorsCorrected { get; set; }
    public long? WriteErrorsTotal { get; set; }
    public long? WriteErrorsCorrected { get; set; }
    public long? ReadLatencyMax { get; set; }
    public long? WriteLatencyMax { get; set; }
}
