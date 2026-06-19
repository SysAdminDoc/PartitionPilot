namespace PartitionPilot;

public class BenchmarkResult
{
    public double SeqWriteMBps { get; set; }
    public double SeqReadMBps { get; set; }
    public double Rnd4kWriteIOPS { get; set; }
    public double Rnd4kReadIOPS { get; set; }
    public double Rnd4kWriteMBps { get; set; }
    public double Rnd4kReadMBps { get; set; }

    public char DriveLetter { get; set; }
    public string DriveModel { get; set; } = "";
    public long DriveCapacityBytes { get; set; }
    public DateTimeOffset Timestamp { get; set; }

    public string ToExportText()
    {
        return $"""
            PartitionPilot Benchmark — {DriveLetter}:\ ({DriveModel})
            Date: {Timestamp:yyyy-MM-dd HH:mm:ss}
            Drive Capacity: {SizeUtil.Format(DriveCapacityBytes)}

            Sequential Write:  {SeqWriteMBps:F1} MB/s
            Sequential Read:   {SeqReadMBps:F1} MB/s
            Random 4K Write:   {Rnd4kWriteIOPS:F0} IOPS  ({Rnd4kWriteMBps:F1} MB/s)
            Random 4K Read:    {Rnd4kReadIOPS:F0} IOPS  ({Rnd4kReadMBps:F1} MB/s)
            """;
    }
}
