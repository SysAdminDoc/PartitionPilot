using System.Text.Json.Serialization;

namespace PartitionPilot;

public class DiskInfo
{
    public int Number { get; set; }
    public string FriendlyName { get; set; } = "";
    public long Size { get; set; }
    public string PartitionStyle { get; set; } = ""; // "MBR", "GPT", "RAW"
    public string UniqueId { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string Path { get; set; } = "";
    public string BusType { get; set; } = "";
    public string Location { get; set; } = "";
    public long LargestFreeExtent { get; set; }
    public int NumberOfPartitions { get; set; }
    public string StoragePoolName { get; set; } = "";
    public string StoragePoolHealth { get; set; } = "";
    public string StoragePoolStatus { get; set; } = "";
    public bool StoragePoolReadOnly { get; set; }
    public bool IsPooled => !string.IsNullOrEmpty(StoragePoolName);
    public bool IsRaw => PartitionStyle.Equals("RAW", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore]
    public bool HasStableIdentity => !string.IsNullOrWhiteSpace(UniqueId) ||
                                     !string.IsNullOrWhiteSpace(SerialNumber) ||
                                     !string.IsNullOrWhiteSpace(Path);
    [JsonIgnore]
    public string DisplayText => IsPooled
        ? $"Disk {Number}: {FriendlyName}  ({SizeUtil.Format(Size)}, {PartitionStyle}, Pool: {StoragePoolName})"
        : $"Disk {Number}: {FriendlyName}  ({SizeUtil.Format(Size)}, {PartitionStyle})";
    [JsonIgnore]
    public string IdentitySummary => ToIdentitySnapshot().StableIdentityText;
    [JsonIgnore]
    public string ConfirmationSummary => $"{DisplayText}{Environment.NewLine}{IdentitySummary}";
    public DiskIdentitySnapshot ToIdentitySnapshot() => DiskIdentitySnapshot.FromDisk(this);
}

public class DiskIdentitySnapshot
{
    public int DiskNumber { get; set; }
    public string FriendlyName { get; set; } = "";
    public long Size { get; set; }
    public string PartitionStyle { get; set; } = "";
    public string UniqueId { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string Path { get; set; } = "";
    public string BusType { get; set; } = "";
    public string Location { get; set; } = "";

    [JsonIgnore]
    public bool HasStableIdentity => !string.IsNullOrWhiteSpace(UniqueId) ||
                                     !string.IsNullOrWhiteSpace(SerialNumber) ||
                                     !string.IsNullOrWhiteSpace(Path);

    [JsonIgnore]
    public string Summary => $"Disk {DiskNumber}: {FriendlyName} ({SizeUtil.Format(Size)}, {PartitionStyle})";

    [JsonIgnore]
    public string StableIdentityText
    {
        get
        {
            var parts = new List<string>();
            AddPart(parts, "UniqueId", UniqueId);
            AddPart(parts, "Serial", SerialNumber);
            AddPart(parts, "Path", Path);
            AddPart(parts, "Bus", BusType);
            AddPart(parts, "Location", Location);
            return parts.Count == 0
                ? "Stable identity: unavailable"
                : $"Stable identity: {string.Join("; ", parts)}";
        }
    }

    [JsonIgnore]
    public string ConfirmationSummary => $"{Summary}{Environment.NewLine}{StableIdentityText}";

    public static DiskIdentitySnapshot FromDisk(DiskInfo disk)
    {
        ArgumentNullException.ThrowIfNull(disk);
        return new DiskIdentitySnapshot
        {
            DiskNumber = disk.Number,
            FriendlyName = disk.FriendlyName,
            Size = disk.Size,
            PartitionStyle = disk.PartitionStyle,
            UniqueId = disk.UniqueId,
            SerialNumber = disk.SerialNumber,
            Path = disk.Path,
            BusType = disk.BusType,
            Location = disk.Location
        };
    }

    public bool Matches(DiskInfo? currentDisk, out string mismatch)
    {
        if (currentDisk is null)
        {
            mismatch = $"Disk {DiskNumber} is not currently connected.";
            return false;
        }

        var mismatches = new List<string>();
        if (DiskNumber != currentDisk.Number)
            mismatches.Add($"disk number changed from {DiskNumber} to {currentDisk.Number}");
        if (Size > 0 && currentDisk.Size > 0 && Size != currentDisk.Size)
            mismatches.Add($"size changed from {SizeUtil.Format(Size)} to {SizeUtil.Format(currentDisk.Size)}");
        CompareStableField(mismatches, "UniqueId", UniqueId, currentDisk.UniqueId);
        CompareStableField(mismatches, "Serial", SerialNumber, currentDisk.SerialNumber);
        CompareStableField(mismatches, "Path", Path, currentDisk.Path);

        if (HasStableIdentity && !currentDisk.HasStableIdentity)
            mismatches.Add("current disk no longer reports a stable identity");

        if (mismatches.Count == 0)
        {
            mismatch = "";
            return true;
        }

        mismatch = string.Join("; ", mismatches.Distinct(StringComparer.OrdinalIgnoreCase));
        return false;
    }

    public async Task VerifyCurrentAsync(IWmiDiskService wmiService)
    {
        var disks = await wmiService.GetDisksAsync();
        var current = disks.FirstOrDefault(d => d.Number == DiskNumber);
        if (!Matches(current, out var mismatch))
            throw new InvalidOperationException($"Target disk identity changed: {mismatch}. Refresh disks and requeue the operation.");
    }

    private static void AddPart(List<string> parts, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            parts.Add($"{label}={TrimForDisplay(value.Trim())}");
    }

    private static void CompareStableField(List<string> mismatches, string label, string expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return;

        if (string.IsNullOrWhiteSpace(actual))
        {
            mismatches.Add($"{label} missing; expected {TrimForDisplay(expected)}");
            return;
        }

        if (!string.Equals(expected.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase))
            mismatches.Add($"{label} changed from {TrimForDisplay(expected)} to {TrimForDisplay(actual)}");
    }

    private static string TrimForDisplay(string value)
    {
        const int max = 96;
        return value.Length <= max ? value : value[..(max - 3)] + "...";
    }
}
