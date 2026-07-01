namespace PartitionPilot;

public sealed record SmartAttributeMetadataView(
    string Name,
    string Severity,
    string Explanation,
    string Recommendation,
    bool HasCuratedMetadata);

public static class SmartAttributeMetadataService
{
    public const string MetadataVersion = "2026.06";

    private const string Info = "Info";
    private const string Warning = "Warning";
    private const string Critical = "Critical";
    private const string Unknown = "Unknown";

    private sealed record AttributeRule(
        string Name,
        string Explanation,
        string Recommendation,
        Func<long, string> Severity);

    private static readonly IReadOnlyDictionary<byte, AttributeRule> AtaRules =
        new Dictionary<byte, AttributeRule>
        {
            [5] = new(
                "Reallocated Sector Count",
                "Counts sectors remapped out of service by drive firmware.",
                "Back up the disk and plan replacement if this value is non-zero or rising.",
                raw => raw > 100 ? Critical : raw > 0 ? Warning : Info),
            [9] = new(
                "Power-On Hours",
                "Approximate lifetime hours reported by the drive.",
                "Use with warranty age, workload, and other warning attributes.",
                _ => Info),
            [12] = new(
                "Power Cycle Count",
                "Number of power cycles reported by the drive.",
                "High counts are informational unless paired with unsafe shutdown or media errors.",
                _ => Info),
            [194] = new(
                "Temperature",
                "Drive temperature in Celsius on most ATA devices.",
                "Improve airflow or reduce workload when temperature remains elevated.",
                raw => raw >= 65 ? Critical : raw >= 55 ? Warning : Info),
            [197] = new(
                "Current Pending Sector Count",
                "Counts unstable sectors waiting for successful rewrite or reallocation.",
                "Back up immediately; rerun surface diagnostics and replace the disk if the value persists.",
                raw => raw > 0 ? Warning : Info),
            [198] = new(
                "Offline Uncorrectable Sector Count",
                "Counts sectors the drive could not correct during offline scans.",
                "Treat non-zero values as media damage evidence and replace the disk after backup.",
                raw => raw > 0 ? Warning : Info),
            [199] = new(
                "UDMA CRC Error Count",
                "Counts host link transfer errors, often caused by bad SATA cables or USB bridge issues.",
                "Check cabling, enclosure, and port stability before blaming the disk media.",
                raw => raw > 0 ? Warning : Info),
            [202] = new(
                "Percent Lifetime Used",
                "Vendor SSD lifetime-used estimate; interpretation varies by model.",
                "Use with the vendor endurance rating and write totals before declaring failure.",
                raw => raw >= 95 ? Critical : raw >= 85 ? Warning : Info),
            [233] = new(
                "Media Wearout Indicator",
                "SSD endurance indicator used by several vendors.",
                "Check vendor documentation; values near the wear limit should trigger replacement planning.",
                raw => raw <= 5 ? Critical : raw <= 15 ? Warning : Info),
            [241] = new(
                "Total LBAs Written",
                "Host writes reported as logical block addresses.",
                "Use with drive sector size to estimate written bytes and endurance consumption.",
                _ => Info),
            [242] = new(
                "Total LBAs Read",
                "Host reads reported as logical block addresses.",
                "Use as workload context; this value is not a health fault by itself.",
                _ => Info)
        };

    public static SmartAttributeMetadataView DescribeAttribute(SmartAttribute attribute)
    {
        if (AtaRules.TryGetValue(attribute.Id, out var rule))
        {
            return new SmartAttributeMetadataView(
                rule.Name,
                rule.Severity(attribute.RawValue),
                rule.Explanation,
                rule.Recommendation,
                true);
        }

        var fallbackName = string.IsNullOrWhiteSpace(attribute.Name)
            ? $"Vendor attribute {attribute.Id}"
            : attribute.Name;
        return new SmartAttributeMetadataView(
            fallbackName,
            Unknown,
            "No curated metadata is available for this vendor-specific SMART attribute.",
            "Keep the raw value visible and compare it with the drive vendor documentation.",
            false);
    }

    public static IReadOnlyList<SmartAdvisory> BuildAdvisories(SmartData data)
    {
        var advisories = new List<SmartAdvisory>();

        foreach (var attribute in data.AllAttributes)
        {
            var metadata = DescribeAttribute(attribute);
            if (!metadata.HasCuratedMetadata || metadata.Severity is Info or Unknown)
                continue;

            advisories.Add(new SmartAdvisory
            {
                Source = attribute.Id == 0 ? "SMART attribute" : $"SMART attribute {attribute.Id}",
                Name = metadata.Name,
                Severity = metadata.Severity,
                Detail = metadata.Explanation,
                Recommendation = metadata.Recommendation,
                RawValue = attribute.RawValue
            });
        }

        AddTopLevel(advisories, "SMART wear", "SSD Wear", data.Wear, PercentUsedSeverity,
            "SSD lifetime-used estimate from SMART/NVMe telemetry.",
            "Plan replacement when wear approaches the vendor endurance limit.");
        AddTopLevel(advisories, "SMART temperature", "Temperature", data.Temperature, TemperatureSeverity,
            "Current drive temperature.",
            "Improve cooling or reduce workload if temperature remains elevated.");
        AddTopLevel(advisories, "NVMe health", "NVMe Available Spare", data.NvmeAvailableSpare, SpareSeverity,
            "Remaining spare capacity reported by the NVMe health log.",
            "Replace the drive when spare capacity remains low or continues falling.");
        AddTopLevel(advisories, "NVMe health", "NVMe Media Errors", data.NvmeMediaErrors, AnyNonZeroWarning,
            "Unrecovered media/data integrity errors reported by the NVMe controller.",
            "Back up immediately and run vendor diagnostics.");
        AddTopLevel(advisories, "NVMe health", "NVMe Error Log Entries", data.NvmeErrorLogEntries, AnyNonZeroWarning,
            "Controller error-log entries reported by the NVMe health log.",
            "Inspect vendor diagnostics and correlate with operating-system storage errors.");

        foreach (var flag in data.CriticalWarningFlags)
        {
            advisories.Add(new SmartAdvisory
            {
                Source = "NVMe critical warning",
                Name = flag,
                Severity = Critical,
                Detail = "NVMe critical-warning bit is set.",
                Recommendation = "Back up immediately and use vendor diagnostics before continuing destructive work.",
                RawValue = data.NvmeCriticalWarning
            });
        }

        return advisories
            .GroupBy(a => $"{a.Source}|{a.Name}|{a.RawValue}")
            .Select(g => g.First())
            .OrderByDescending(a => SeverityRank(a.Severity))
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddTopLevel(
        List<SmartAdvisory> advisories,
        string source,
        string name,
        long? value,
        Func<long, string> severity,
        string detail,
        string recommendation)
    {
        if (value is null) return;
        var level = severity(value.Value);
        if (level is Info or Unknown) return;

        advisories.Add(new SmartAdvisory
        {
            Source = source,
            Name = name,
            Severity = level,
            Detail = detail,
            Recommendation = recommendation,
            RawValue = value
        });
    }

    private static string PercentUsedSeverity(long value) =>
        value >= 95 ? Critical : value >= 85 ? Warning : Info;

    private static string TemperatureSeverity(long value) =>
        value >= 65 ? Critical : value >= 55 ? Warning : Info;

    private static string SpareSeverity(long value) =>
        value <= 5 ? Critical : value <= 20 ? Warning : Info;

    private static string AnyNonZeroWarning(long value) => value > 0 ? Warning : Info;

    private static int SeverityRank(string severity) => severity switch
    {
        Critical => 3,
        Warning => 2,
        Info => 1,
        _ => 0
    };
}
