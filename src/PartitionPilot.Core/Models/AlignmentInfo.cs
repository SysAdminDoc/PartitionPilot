namespace PartitionPilot;

public class AlignmentInfo
{
    public int DiskNumber { get; set; }
    public int PartitionNumber { get; set; }
    public char? DriveLetter { get; set; }
    public long Offset { get; set; }
    public bool IsAligned { get; set; }
    public string Status { get; set; } = "";

    public string PartitionDisplay => $"Disk {DiskNumber}, Partition {PartitionNumber}";
    public string LetterDisplay => DriveLetter.HasValue ? $"{DriveLetter}:" : "—";
    public string OffsetDisplay => $"{Offset:N0} bytes";
    public string AlignedDisplay => IsAligned ? "Yes" : "No";
}
