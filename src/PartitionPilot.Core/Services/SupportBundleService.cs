using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PartitionPilot;

public sealed record SupportBundleOptions(
    string OutputZipPath,
    string AppVersion,
    string ElevationContext,
    string ActivityLogText,
    string SnapshotDirectory,
    bool IsAdmin,
    DateTimeOffset Timestamp);

public static class SupportBundleService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex JsonSerialPattern = new("""(?i)("serial(?:number)?"\s*:\s*")[^"]*(")""", RegexOptions.Compiled);
    private static readonly Regex TextSerialPattern = new("""(?i)(serial(?:number)?\s*[:=]\s*)[^\s,;]+""", RegexOptions.Compiled);
    private static readonly Regex SupportPathPattern = new("""(?i)[A-Z]:\\[^\r\n'"]+""", RegexOptions.Compiled);

    public static async Task CreateAsync(
        SupportBundleOptions options,
        IWmiDiskService wmiService,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.OutputZipPath))
            throw new ArgumentException("Output zip path is required.", nameof(options));

        var tempDir = Path.Combine(Path.GetTempPath(), $"pp_support_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var info = new
            {
                options.AppVersion,
                OSVersion = Environment.OSVersion.VersionString,
                OSBuild = Environment.OSVersion.Version.Build,
                Is64Bit = Environment.Is64BitOperatingSystem,
                options.IsAdmin,
                options.ElevationContext,
                DotNetVersion = Environment.Version.ToString(),
                Timestamp = options.Timestamp.ToString("o")
            };

            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "system-info.json"),
                JsonSerializer.Serialize(info, JsonOptions),
                ct);

            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "activity-log.txt"),
                RedactText(options.ActivityLogText),
                ct);

            var disks = await wmiService.GetDisksAsync();
            var redactedDisks = disks.Select(d => new
            {
                d.Number,
                d.FriendlyName,
                Size = SizeUtil.Format(d.Size),
                d.PartitionStyle,
                d.NumberOfPartitions,
                d.StoragePoolName
            });

            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "disk-summary.json"),
                JsonSerializer.Serialize(redactedDisks, JsonOptions),
                ct);

            await CopySnapshotsAsync(options.SnapshotDirectory, tempDir, ct);

            var outputFull = Path.GetFullPath(options.OutputZipPath);
            var outputParent = Path.GetDirectoryName(outputFull);
            if (!string.IsNullOrWhiteSpace(outputParent))
                Directory.CreateDirectory(outputParent);
            if (File.Exists(outputFull))
                File.Delete(outputFull);

            ZipFile.CreateFromDirectory(tempDir, outputFull);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    public static string RedactText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var redacted = SupportPathPattern.Replace(text, "[path]");
        redacted = JsonSerialPattern.Replace(redacted, "$1[redacted]$2");
        redacted = TextSerialPattern.Replace(redacted, "$1[redacted]");
        return redacted;
    }

    private static async Task CopySnapshotsAsync(string snapshotDirectory, string tempDir, CancellationToken ct)
    {
        if (!Directory.Exists(snapshotDirectory))
            return;

        var snapshotOut = Path.Combine(tempDir, "snapshots");
        Directory.CreateDirectory(snapshotOut);
        foreach (var file in Directory.EnumerateFiles(snapshotDirectory, "*.json")
                     .OrderByDescending(File.GetCreationTimeUtc)
                     .Take(10))
        {
            var content = await File.ReadAllTextAsync(file, ct);
            content = RedactText(content);
            await File.WriteAllTextAsync(Path.Combine(snapshotOut, Path.GetFileName(file)), content, ct);
        }
    }
}
