namespace PartitionPilot;

public class BenchmarkResult
{
    public double SeqWriteMBps { get; set; }
    public double SeqReadMBps { get; set; }
    public double Rnd4kWriteIOPS { get; set; }
    public double Rnd4kReadIOPS { get; set; }
    public double Rnd4kWriteMBps { get; set; }
    public double Rnd4kReadMBps { get; set; }
}
