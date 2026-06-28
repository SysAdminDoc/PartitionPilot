using System.IO;

namespace PartitionPilot;

public class PartitionSnapshot
{
    public string FilePath { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public int DiskNumber { get; set; }
    public string DiskName { get; set; } = "";
    public long DiskSize { get; set; }
    public string PartitionStyle { get; set; } = "";
    public DiskIdentitySnapshot? DiskIdentity { get; set; }
    public List<PartitionSnapshotPartition> Partitions { get; set; } = new();

    public string FileName => string.IsNullOrWhiteSpace(FilePath) ? "" : Path.GetFileName(FilePath);
    public DateTimeOffset? CapturedAt => DateTimeOffset.TryParse(Timestamp, out var parsed) ? parsed : null;
    public string CapturedAtText => CapturedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown time";
    public DiskIdentitySnapshot EffectiveDiskIdentity => DiskIdentity ?? new DiskIdentitySnapshot
    {
        DiskNumber = DiskNumber,
        FriendlyName = DiskName,
        Size = DiskSize,
        PartitionStyle = PartitionStyle
    };
    public string DiskSummary => EffectiveDiskIdentity.Summary;
    public string PartitionCountText => Partitions.Count == 1 ? "1 partition" : $"{Partitions.Count} partitions";
}

public class PartitionSnapshotPartition
{
    public int PartitionNumber { get; set; }
    public string? DriveLetter { get; set; }
    public string Label { get; set; } = "";
    public long Size { get; set; }
    public long Offset { get; set; }
    public string Type { get; set; } = "";
    public string FileSystem { get; set; } = "";
    public bool IsBoot { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
    public bool IsHidden { get; set; }

    public string LetterDisplay => string.IsNullOrWhiteSpace(DriveLetter) ? "-" : $"{DriveLetter}:";
    public string SizeText => SizeUtil.Format(Size);
    public string OffsetText => SizeUtil.Format(Offset);
    public string RoleText
    {
        get
        {
            var roles = new List<string>();
            if (IsBoot) roles.Add("Boot");
            if (IsSystem) roles.Add("System");
            if (IsActive) roles.Add("Active");
            if (IsHidden) roles.Add("Hidden");
            return roles.Count == 0 ? Type : string.Join(", ", roles);
        }
    }
}
