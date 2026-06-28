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

    public static List<LayoutDiffEntry> ComputeDiff(PartitionLayoutSpec spec, DiskInfo disk, List<PartitionInfo> currentPartitions)
    {
        var normalized = ValidateAndNormalize(spec);
        var diff = new List<LayoutDiffEntry>();

        if (normalized.Style == "GPT" && disk.PartitionStyle == "MBR")
        {
            diff.Add(new LayoutDiffEntry
            {
                Action = "Convert",
                Description = $"Convert Disk {disk.Number} from MBR to GPT",
                RiskLevel = "High"
            });
        }

        if (currentPartitions.Count > 0 && normalized.Partitions.Count > 0)
        {
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
        else if (disk.PartitionStyle == "RAW" && normalized.Partitions.Count > 0)
        {
            diff.Add(new LayoutDiffEntry
            {
                Action = "Initialize",
                Description = $"Initialize RAW Disk {disk.Number} as {normalized.Style}",
                DiskpartScript = $"select disk {disk.Number}\nconvert {normalized.Style.ToLowerInvariant()}"
            });
        }

        for (int i = 0; i < normalized.Partitions.Count; i++)
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

    private sealed record NormalizedLayoutSpec(string Style, List<NormalizedPartitionSpec> Partitions);

    private sealed record NormalizedPartitionSpec(
        long? SizeMB,
        bool UseMaximumSize,
        string FileSystem,
        string Label,
        char? DriveLetter);
}
