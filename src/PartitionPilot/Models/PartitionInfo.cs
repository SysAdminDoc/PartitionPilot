namespace PartitionPilot;

public class PartitionInfo
{
    public int PartitionNumber { get; set; }
    public char? DriveLetter { get; set; }
    public string Label { get; set; } = "";
    public long Size { get; set; }
    public long FreeSpace { get; set; }
    public string Type { get; set; } = "Basic"; // "Basic", "System", "Recovery", "Reserved"
    public string FileSystem { get; set; } = "";
    public long Offset { get; set; }
    public bool IsBoot { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
    public bool IsHidden { get; set; }
    public bool HasPagefile { get; set; }
    public int DiskNumber { get; set; }

    public string LetterDisplay => DriveLetter.HasValue ? $"{DriveLetter}:" : "—";

    public string SizeText => SizeUtil.Format(Size);

    public string FreeText => FreeSpace > 0 ? SizeUtil.Format(FreeSpace) : "—";

    public string Details
    {
        get
        {
            var parts = new List<string>();
            if (IsBoot) parts.Add("Boot");
            if (IsSystem) parts.Add("System");
            if (IsActive) parts.Add("Active");
            if (IsHidden) parts.Add("Hidden");
            if (HasPagefile) parts.Add("Pagefile");
            return parts.Count > 0 ? string.Join(", ", parts) : Type;
        }
    }
}
