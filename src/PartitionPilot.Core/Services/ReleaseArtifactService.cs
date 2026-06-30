using System.Security.Cryptography;
using System.Text.Json;

namespace PartitionPilot;

public sealed class ReleaseArtifactManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string AppVersion { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; }
    public bool IsLocalTestBuild { get; set; }
    public string SigningStatus { get; set; } = "";
    public List<ReleaseArtifactEntry> Artifacts { get; set; } = new();
}

public sealed class ReleaseArtifactEntry
{
    public string FileName { get; set; } = "";
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
    public string AuthenticodeStatus { get; set; } = "";
}

public sealed record ReleaseManifestVerificationResult(bool IsValid, IReadOnlyList<string> Errors);

public static class ReleaseArtifactService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string[] ManifestFileNames = ["SHA256SUMS", "SHA256SUMS.json"];

    public static async Task<ReleaseArtifactManifest> CreateManifestAsync(
        string artifactsDir,
        string appVersion,
        string? certThumbprint = null,
        string? timestampUrl = null,
        IProcessRunner? runner = null,
        IActivityLog? log = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(artifactsDir))
            throw new ArgumentException("Artifacts directory is required.", nameof(artifactsDir));

        if (!Directory.Exists(artifactsDir))
            throw new DirectoryNotFoundException($"Artifacts directory not found: {artifactsDir}");

        var files = Directory.EnumerateFiles(artifactsDir, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !ManifestFileNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
            throw new InvalidOperationException("No release artifacts found to hash.");

        certThumbprint = FirstNonEmpty(certThumbprint, Environment.GetEnvironmentVariable("PARTITIONPILOT_SIGN_CERT_THUMBPRINT"));
        timestampUrl = FirstNonEmpty(timestampUrl, Environment.GetEnvironmentVariable("PARTITIONPILOT_TIMESTAMP_URL"));
        var signingConfigured = !string.IsNullOrWhiteSpace(certThumbprint);

        if (signingConfigured)
        {
            runner ??= new ProcessRunner();
            foreach (var exe in files.Where(path => Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)))
                await SignExecutableAsync(exe, certThumbprint!, timestampUrl, runner, log, ct);
        }

        var entries = new List<ReleaseArtifactEntry>();
        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            entries.Add(new ReleaseArtifactEntry
            {
                FileName = info.Name,
                Length = info.Length,
                Sha256 = await ComputeSha256HexAsync(path, ct),
                AuthenticodeStatus = signingConfigured && info.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                    ? "Signed"
                    : "UnsignedLocalTest"
            });
        }

        var manifest = new ReleaseArtifactManifest
        {
            AppVersion = appVersion,
            GeneratedAt = DateTimeOffset.UtcNow,
            IsLocalTestBuild = !signingConfigured,
            SigningStatus = signingConfigured ? "SignedWithConfiguredCertificate" : "UnsignedLocalTest",
            Artifacts = entries
        };

        await WriteManifestFilesAsync(artifactsDir, manifest, ct);
        log?.Log(signingConfigured
            ? $"Release artifact manifest written with Authenticode signing metadata: {artifactsDir}"
            : $"Release artifact manifest written as unsigned local-test build: {artifactsDir}");

        return manifest;
    }

    public static async Task<ReleaseManifestVerificationResult> VerifyManifestAsync(
        string artifactsDir,
        CancellationToken ct = default)
    {
        var manifestPath = Path.Combine(artifactsDir, "SHA256SUMS.json");
        if (!File.Exists(manifestPath))
            return new ReleaseManifestVerificationResult(false, [$"Manifest not found: {manifestPath}"]);

        var manifest = JsonSerializer.Deserialize<ReleaseArtifactManifest>(
            await File.ReadAllTextAsync(manifestPath, ct),
            JsonOptions);

        if (manifest is null)
            return new ReleaseManifestVerificationResult(false, ["Manifest could not be parsed."]);

        var errors = new List<string>();
        foreach (var artifact in manifest.Artifacts)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(artifactsDir, artifact.FileName);
            if (!File.Exists(path))
            {
                errors.Add($"Missing artifact: {artifact.FileName}");
                continue;
            }

            var actual = await ComputeSha256HexAsync(path, ct);
            if (!string.Equals(actual, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                errors.Add($"SHA256 mismatch for {artifact.FileName}: expected {artifact.Sha256}, got {actual}");
        }

        return new ReleaseManifestVerificationResult(errors.Count == 0, errors);
    }

    public static async Task<string> ComputeSha256HexAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    private static async Task SignExecutableAsync(
        string path,
        string certThumbprint,
        string? timestampUrl,
        IProcessRunner runner,
        IActivityLog? log,
        CancellationToken ct)
    {
        var signtool = FirstNonEmpty(Environment.GetEnvironmentVariable("SIGNTOOL_EXE"), "signtool.exe")!;
        var timestampArgs = string.IsNullOrWhiteSpace(timestampUrl)
            ? ""
            : $" /tr \"{timestampUrl}\" /td SHA256";
        var args = $"sign /fd SHA256 /sha1 {certThumbprint}{timestampArgs} \"{path}\"";
        log?.Log($"Signing release artifact: {Path.GetFileName(path)}");
        await runner.RunExeAsync(signtool, args, log, ct: ct);
    }

    private static async Task WriteManifestFilesAsync(string artifactsDir, ReleaseArtifactManifest manifest, CancellationToken ct)
    {
        var textPath = Path.Combine(artifactsDir, "SHA256SUMS");
        var jsonPath = Path.Combine(artifactsDir, "SHA256SUMS.json");

        var lines = manifest.Artifacts.Select(entry => $"{entry.Sha256}  {entry.FileName}");
        await File.WriteAllLinesAsync(textPath, lines, ct);
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(manifest, JsonOptions), ct);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
