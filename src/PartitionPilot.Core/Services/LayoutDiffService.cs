using System.Globalization;
using System.Text.RegularExpressions;

namespace PartitionPilot;

public class LayoutDiffEntry
{
    public string Action { get; set; } = "";
    public string Description { get; set; } = "";
    public string DiskpartScript { get; set; } = "";
    public string RiskLevel { get; set; } = "Normal";
}

public static class LayoutDiffService
{
    private static readonly Regex SizeMbPattern = new(@"^[0-9]+$", RegexOptions.Compiled);

    public static List<LayoutDiffEntry> ComputeDiff(
        PartitionLayoutSpec spec,
        DiskInfo disk,
        List<PartitionInfo> currentPartitions,
        bool allowDestructiveReplace = false)
    {
        var normalized = ValidateAndNormalize(spec);
        var diff = new List<LayoutDiffEntry>();

        var hasPartitions = currentPartitions.Count > 0;
        var styleMatches = disk.PartitionStyle.Equals(normalized.Style, StringComparison.OrdinalIgnoreCase);
        var needsInitialize = normalized.Partitions.Count > 0 &&
            (disk.PartitionStyle.Equals("RAW", StringComparison.OrdinalIgnoreCase) || (!styleMatches && !hasPartitions));

        if (hasPartitions && !LayoutMatches(normalized, disk, currentPartitions, out var mismatchReason))
        {
            if (!allowDestructiveReplace)
            {
                diff.Add(new LayoutDiffEntry
                {
                    Action = "Blocked",
                    Description = $"Current layout differs from spec: {mismatchReason}. Rerun with --replace to clear and recreate the disk.",
                    RiskLevel = "Blocked"
                });
                return diff;
            }

            diff.Add(new LayoutDiffEntry
            {
                Action = "Clear",
                Description = $"Clear Disk {disk.Number} (remove {currentPartitions.Count} existing partition(s))",
                DiskpartScript = $"select disk {disk.Number}\nclean",
                RiskLevel = "Destructive"
            });

            diff.Add(new LayoutDiffEntry
            {
                Action = "Initialize",
                Description = $"Initialize Disk {disk.Number} as {normalized.Style}",
                DiskpartScript = $"select disk {disk.Number}\nconvert {normalized.Style.ToLowerInvariant()}"
            });
        }
        else if (needsInitialize)
        {
            diff.Add(new LayoutDiffEntry
            {
                Action = "Initialize",
                Description = $"Initialize RAW Disk {disk.Number} as {normalized.Style}",
                DiskpartScript = $"select disk {disk.Number}\nconvert {normalized.Style.ToLowerInvariant()}"
            });
        }

        var createStartIndex = hasPartitions && !allowDestructiveReplace
            ? currentPartitions.Count
            : 0;

        for (int i = createStartIndex; i < normalized.Partitions.Count; i++)
        {
            var part = normalized.Partitions[i];
            var sizeClause = part.UseMaximumSize ? "" : $" size={part.SizeMB!.Value.ToString(CultureInfo.InvariantCulture)}";
            var letterClause = part.DriveLetter.HasValue ? $"\nassign letter={part.DriveLetter.Value}" : "\nassign";

            diff.Add(new LayoutDiffEntry
            {
                Action = "Create",
                Description = $"Create{(part.UseMaximumSize ? " max-size" : $" {part.SizeMB} MB")} {part.FileSystem} partition" +
                    (part.Label.Length > 0 ? $" \"{part.Label}\"" : "") +
                    (part.DriveLetter.HasValue ? $" ({part.DriveLetter}:)" : ""),
                DiskpartScript = $"select disk {disk.Number}\ncreate partition primary{sizeClause}\nformat fs={part.FileSystem}{(part.Label.Length > 0 ? $" label=\"{part.Label}\"" : "")} quick{letterClause}"
            });
        }

        return diff;
    }

    public static string FormatPlan(List<LayoutDiffEntry> diff)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Plan: {diff.Count} step(s)");
        sb.AppendLine(new string('-', 50));
        if (diff.Count == 0)
        {
            sb.AppendLine("  No changes needed.");
            return sb.ToString();
        }

        for (int i = 0; i < diff.Count; i++)
        {
            var d = diff[i];
            sb.AppendLine($"  {i + 1}. [{d.Action}] {d.Description}");
            if (d.RiskLevel != "Normal")
                sb.AppendLine($"     Risk: {d.RiskLevel}");
        }
        return sb.ToString();
    }

    public static void Validate(PartitionLayoutSpec spec) => _ = ValidateAndNormalize(spec);

    private static NormalizedLayoutSpec ValidateAndNormalize(PartitionLayoutSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var style = ValidateStyle(spec.Style);
        if (spec.Partitions is null)
            throw new ArgumentException("Layout spec partitions are required.", nameof(spec));

        var partitions = spec.Partitions
            .Select((partition, index) => ValidateAndNormalizePartition(partition, index))
            .ToList();

        return new NormalizedLayoutSpec(style, partitions);
    }

    private static string ValidateStyle(string? style)
    {
        var normalized = (style ?? "").Trim().ToUpperInvariant();
        if (normalized is not ("GPT" or "MBR"))
            throw new ArgumentException("Layout style must be GPT or MBR.");
        return normalized;
    }

    private static NormalizedPartitionSpec ValidateAndNormalizePartition(PartitionSpec? partition, int index)
    {
        if (partition is null)
            throw new ArgumentException($"Partition {index + 1} is required.");

        var fileSystem = ProcessRunner.ValidateFileSystem((partition.FileSystem ?? "").Trim());
        var createCapability = FilesystemCapabilityService.Evaluate(fileSystem, FilesystemOperation.Create);
        if (!createCapability.IsAllowed)
            throw new ArgumentException($"Partition {index + 1}: {createCapability.Reason}");

        var label = ProcessRunner.SanitizeLabel(partition.Label ?? "");
        var sizeMb = ValidateSizeMb(partition.SizeMB, partition.UseMaximumSize, index);
        var driveLetter = ValidateDriveLetter(partition.DriveLetter, index);

        return new NormalizedPartitionSpec(sizeMb, partition.UseMaximumSize, fileSystem, label, driveLetter);
    }

    private static long? ValidateSizeMb(string? sizeMb, bool useMaximumSize, int index)
    {
        if (useMaximumSize)
        {
            if (!string.IsNullOrWhiteSpace(sizeMb))
                throw new ArgumentException($"Partition {index + 1} cannot set SizeMB when UseMaximumSize is true.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(sizeMb))
            throw new ArgumentException($"Partition {index + 1} SizeMB is required unless UseMaximumSize is true.");

        var trimmed = sizeMb.Trim();
        if (!SizeMbPattern.IsMatch(trimmed) ||
            !long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value <= 0 ||
            value > int.MaxValue)
            throw new ArgumentException($"Partition {index + 1} SizeMB must be a positive whole number of megabytes.");

        return value;
    }

    private static char? ValidateDriveLetter(string? driveLetter, int index)
    {
        if (string.IsNullOrWhiteSpace(driveLetter))
            return null;

        var trimmed = driveLetter.Trim();
        if (trimmed.Length == 2 && trimmed[1] == ':')
            trimmed = trimmed[..1];

        if (trimmed.Length != 1)
            throw new ArgumentException($"Partition {index + 1} DriveLetter must be a single letter A-Z.");

        var letter = char.ToUpperInvariant(trimmed[0]);
        if (letter < 'A' || letter > 'Z')
            throw new ArgumentException($"Partition {index + 1} DriveLetter must be a single letter A-Z.");

        return letter;
    }

    private static bool LayoutMatches(
        NormalizedLayoutSpec spec,
        DiskInfo disk,
        List<PartitionInfo> currentPartitions,
        out string mismatchReason)
    {
        mismatchReason = "";

        if (!disk.PartitionStyle.Equals(spec.Style, StringComparison.OrdinalIgnoreCase))
        {
            mismatchReason = $"disk style is {disk.PartitionStyle}, expected {spec.Style}";
            return false;
        }

        if (currentPartitions.Count > spec.Partitions.Count)
        {
            mismatchReason = $"disk has {currentPartitions.Count} partition(s), spec has {spec.Partitions.Count}";
            return false;
        }

        for (int i = 0; i < currentPartitions.Count; i++)
        {
            if (!PartitionMatches(spec.Partitions[i], currentPartitions[i], i, out mismatchReason))
                return false;
        }

        return true;
    }

    private static bool PartitionMatches(
        NormalizedPartitionSpec expected,
        PartitionInfo actual,
        int index,
        out string mismatchReason)
    {
        mismatchReason = "";

        if (!string.Equals(actual.FileSystem, expected.FileSystem, StringComparison.OrdinalIgnoreCase))
        {
            mismatchReason = $"partition {index + 1} filesystem is {actual.FileSystemDisplay}, expected {expected.FileSystem}";
            return false;
        }

        if (!string.Equals(actual.Label ?? "", expected.Label, StringComparison.Ordinal))
        {
            mismatchReason = $"partition {index + 1} label is \"{actual.Label}\", expected \"{expected.Label}\"";
            return false;
        }

        if (expected.DriveLetter.HasValue && actual.DriveLetter != expected.DriveLetter)
        {
            mismatchReason = $"partition {index + 1} letter is {actual.LetterDisplay}, expected {expected.DriveLetter}:";
            return false;
        }

        if (!expected.UseMaximumSize)
        {
            var expectedBytes = expected.SizeMB!.Value * 1024L * 1024L;
            var toleranceBytes = 1024L * 1024L;
            if (Math.Abs(actual.Size - expectedBytes) > toleranceBytes)
            {
                mismatchReason = $"partition {index + 1} size is {SizeUtil.Format(actual.Size)}, expected {expected.SizeMB} MB";
                return false;
            }
        }

        return true;
    }

    private sealed record NormalizedLayoutSpec(string Style, List<NormalizedPartitionSpec> Partitions);

    private sealed record NormalizedPartitionSpec(
        long? SizeMB,
        bool UseMaximumSize,
        string FileSystem,
        string Label,
        char? DriveLetter);
}
