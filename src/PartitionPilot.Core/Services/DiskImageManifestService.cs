using System.Security.Cryptography;
using System.Text.Json;

namespace PartitionPilot;

public sealed class DiskImageManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string AppVersion { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string ImageFileName { get; set; } = "";
    public string ImageSha256 { get; set; } = "";
    public bool IsEncrypted { get; set; }
    public string PlainImageSha256 { get; set; } = "";
    public string SourceDrive { get; set; } = "";
    public string SourceFileSystem { get; set; } = "";
    public string SourceLabel { get; set; } = "";
    public long SourceVolumeBytes { get; set; }
    public long SourceFreeBytes { get; set; }
    public long SourceFileCount { get; set; }
    public long SourceTotalBytes { get; set; }
    public string VerificationMode { get; set; } = "Sampled";
    public List<DiskImageSampleHash> SampleHashes { get; set; } = new();
}

public sealed class DiskImageSampleHash
{
    public string RelativePath { get; set; } = "";
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed record DiskImageManifestValidation(
    bool IsValid,
    string Status,
    string Detail,
    DiskImageManifest? Manifest);

public static class DiskImageManifestService
{
    private const int MaxSampleHashes = 32;
    private const long MaxSampleHashBytes = 64L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string GetManifestPath(string imagePath) => imagePath + ".ppmanifest.json";

    public static async Task<DiskImageManifest> CreateManifestAsync(
        string imagePath,
        char sourceDrive,
        string sourceRoot,
        VolumeInfo? sourceVolume,
        string appVersion,
        IActivityLog? log = null,
        CancellationToken ct = default)
    {
        var sourceStats = await CollectSourceStatsAsync(sourceRoot, ct);
        var manifest = new DiskImageManifest
        {
            AppVersion = appVersion,
            CreatedAt = DateTimeOffset.UtcNow,
            ImageFileName = Path.GetFileName(imagePath),
            ImageSha256 = await ComputeSha256HexAsync(imagePath, ct),
            SourceDrive = $"{char.ToUpperInvariant(sourceDrive)}:",
            SourceFileSystem = sourceVolume?.FileSystemType ?? "",
            SourceLabel = sourceVolume?.FileSystemLabel ?? "",
            SourceVolumeBytes = sourceVolume?.Size ?? 0,
            SourceFreeBytes = sourceVolume?.SizeRemaining ?? 0,
            SourceFileCount = sourceStats.FileCount,
            SourceTotalBytes = sourceStats.TotalBytes,
            SampleHashes = sourceStats.SampleHashes
        };

        await WriteManifestAsync(imagePath, manifest, ct);
        log?.Log($"Image manifest written: {GetManifestPath(imagePath)}");
        return manifest;
    }

    public static async Task<DiskImageManifest> RebindManifestToEncryptedImageAsync(
        DiskImageManifest manifest,
        string encryptedImagePath,
        CancellationToken ct = default)
    {
        manifest.PlainImageSha256 = manifest.ImageSha256;
        manifest.ImageFileName = Path.GetFileName(encryptedImagePath);
        manifest.ImageSha256 = await ComputeSha256HexAsync(encryptedImagePath, ct);
        manifest.IsEncrypted = true;
        await WriteManifestAsync(encryptedImagePath, manifest, ct);
        return manifest;
    }

    public static async Task<DiskImageManifestValidation> ValidateManifestAsync(
        string imagePath,
        CancellationToken ct = default)
    {
        var manifestPath = GetManifestPath(imagePath);
        if (!File.Exists(manifestPath))
            return new DiskImageManifestValidation(false, "Missing", $"Image manifest not found: {manifestPath}", null);

        DiskImageManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<DiskImageManifest>(
                await File.ReadAllTextAsync(manifestPath, ct),
                JsonOptions);
        }
        catch (Exception ex)
        {
            return new DiskImageManifestValidation(false, "Unreadable", $"Image manifest could not be read: {ex.Message}", null);
        }

        if (manifest is null)
            return new DiskImageManifestValidation(false, "Unreadable", "Image manifest could not be parsed.", null);

        var actual = await ComputeSha256HexAsync(imagePath, ct);
        if (!string.Equals(actual, manifest.ImageSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new DiskImageManifestValidation(
                false,
                "HashMismatch",
                $"Image SHA256 mismatch. Expected {manifest.ImageSha256}, got {actual}.",
                manifest);
        }

        return new DiskImageManifestValidation(true, "Valid", "Image manifest SHA256 matches the image file.", manifest);
    }

    public static async Task<string> ComputeSha256HexAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    private static async Task WriteManifestAsync(string imagePath, DiskImageManifest manifest, CancellationToken ct)
    {
        await File.WriteAllTextAsync(GetManifestPath(imagePath), JsonSerializer.Serialize(manifest, JsonOptions), ct);
    }

    private static async Task<SourceStats> CollectSourceStatsAsync(string sourceRoot, CancellationToken ct)
    {
        var stats = new SourceStats();
        if (!Directory.Exists(sourceRoot))
            return stats;

        foreach (var path in EnumerateFilesSafely(sourceRoot))
        {
            ct.ThrowIfCancellationRequested();
            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (!info.Exists)
                    continue;
            }
            catch
            {
                continue;
            }

            stats.FileCount++;
            stats.TotalBytes += info.Length;

            if (stats.SampleHashes.Count >= MaxSampleHashes || info.Length > MaxSampleHashBytes)
                continue;

            try
            {
                stats.SampleHashes.Add(new DiskImageSampleHash
                {
                    RelativePath = Path.GetRelativePath(sourceRoot, info.FullName),
                    Length = info.Length,
                    Sha256 = await ComputeSha256HexAsync(info.FullName, ct)
                });
            }
            catch
            {
                // Sampling is evidence, not a reason to fail a capture.
            }
        }

        return stats;
    }

    private static IEnumerable<string> EnumerateFilesSafely(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> files = [];
            IEnumerable<string> dirs = [];

            try { files = Directory.EnumerateFiles(current); } catch { }
            foreach (var file in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                yield return file;

            try { dirs = Directory.EnumerateDirectories(current); } catch { }
            foreach (var dir in dirs.OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
                pending.Push(dir);
        }
    }

    private sealed class SourceStats
    {
        public long FileCount { get; set; }
        public long TotalBytes { get; set; }
        public List<DiskImageSampleHash> SampleHashes { get; } = new();
    }
}
