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
    public static List<LayoutDiffEntry> ComputeDiff(PartitionLayoutSpec spec, DiskInfo disk, List<PartitionInfo> currentPartitions)
    {
        var diff = new List<LayoutDiffEntry>();

        if (spec.Style.Equals("GPT", StringComparison.OrdinalIgnoreCase) && disk.PartitionStyle == "MBR")
        {
            diff.Add(new LayoutDiffEntry
            {
                Action = "Convert",
                Description = $"Convert Disk {disk.Number} from MBR to GPT",
                RiskLevel = "High"
            });
        }

        if (currentPartitions.Count > 0 && spec.Partitions.Count > 0)
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
                Description = $"Initialize Disk {disk.Number} as {spec.Style}",
                DiskpartScript = $"select disk {disk.Number}\nconvert {spec.Style.ToLowerInvariant()}"
            });
        }
        else if (disk.PartitionStyle == "RAW" && spec.Partitions.Count > 0)
        {
            diff.Add(new LayoutDiffEntry
            {
                Action = "Initialize",
                Description = $"Initialize RAW Disk {disk.Number} as {spec.Style}",
                DiskpartScript = $"select disk {disk.Number}\nconvert {spec.Style.ToLowerInvariant()}"
            });
        }

        for (int i = 0; i < spec.Partitions.Count; i++)
        {
            var part = spec.Partitions[i];
            var fs = ProcessRunner.ValidateFileSystem(part.FileSystem);
            var label = ProcessRunner.SanitizeLabel(part.Label);
            var sizeClause = part.UseMaximumSize ? "" : $" size={part.SizeMB}";
            var letterClause = !string.IsNullOrEmpty(part.DriveLetter) ? $"\nassign letter={part.DriveLetter.ToUpperInvariant()}" : "\nassign";

            diff.Add(new LayoutDiffEntry
            {
                Action = "Create",
                Description = $"Create{(part.UseMaximumSize ? " max-size" : $" {part.SizeMB} MB")} {fs} partition" +
                    (label.Length > 0 ? $" \"{label}\"" : "") +
                    (!string.IsNullOrEmpty(part.DriveLetter) ? $" ({part.DriveLetter}:)" : ""),
                DiskpartScript = $"select disk {disk.Number}\ncreate partition primary{sizeClause}\nformat fs={fs}{(label.Length > 0 ? $" label=\"{label}\"" : "")} quick{letterClause}"
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
}
