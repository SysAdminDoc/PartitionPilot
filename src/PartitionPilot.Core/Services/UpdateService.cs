using System.Reflection;
using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace PartitionPilot;

public sealed record ReleaseVerificationResult(string Status, string Detail)
{
    public bool IsVerified => Status.Equals("Verified", StringComparison.OrdinalIgnoreCase);
}

public static class UpdateService
{
    private const string RepoUrl = "https://github.com/SysAdminDoc/PartitionPilot";
    private static readonly string LatestReleaseApiUrl = BuildLatestReleaseApiUrl(RepoUrl);

    public static string GetCurrentVersion()
    {
        var informational = typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            .Split('+', 2)[0];

        if (Version.TryParse(informational, out _))
            return informational!;

        return typeof(UpdateService).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    public static bool IsNewerVersion(string latestVersion, string currentVersion) =>
        Version.TryParse(latestVersion, out var latest) &&
        Version.TryParse(currentVersion, out var current) &&
        latest > current;

    public static async Task<UpdateInfo?> CheckForVelopackUpdateAsync(IActivityLog? log = null)
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
            var update = await mgr.CheckForUpdatesAsync();
            if (update is not null)
            {
                log?.Log($"Velopack update available: v{update.TargetFullRelease.Version}");
            }
            return update;
        }
        catch (Exception ex)
        {
            log?.Log($"Velopack update check failed: {ex.Message}");
            return null;
        }
    }

    public static async Task DownloadAndApplyAsync(UpdateInfo update, IActivityLog? log = null)
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
            log?.Log($"Downloading update v{update.TargetFullRelease.Version}...");
            var expectedHash = FirstNonEmpty(update.TargetFullRelease.SHA256, update.TargetFullRelease.SHA1);
            if (string.IsNullOrWhiteSpace(expectedHash))
                throw new InvalidOperationException("Update package does not publish an expected checksum.");

            await mgr.DownloadUpdatesAsync(update);
            var hashStatus = string.IsNullOrWhiteSpace(update.TargetFullRelease.SHA256)
                ? "SHA1 checksum verified by Velopack; SHA256 unavailable in release metadata."
                : "SHA256 checksum verified by Velopack.";
            log?.Log($"Update downloaded. {hashStatus} Applying on next restart.");
        }
        catch (Exception ex)
        {
            log?.Log($"Update download failed: {ex.Message}");
            throw;
        }
    }

    public static void ApplyAndRestart(UpdateInfo update, IActivityLog? log = null)
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
            log?.Log("Applying update and restarting...");
            mgr.ApplyUpdatesAndRestart(update);
        }
        catch (Exception ex)
        {
            log?.Log($"Update apply failed: {ex.Message}");
            throw;
        }
    }

    public static async Task<(bool available, string version, string url, string verificationStatus, string verificationDetail)?> CheckForUpdateAsync()
    {
        try
        {
            var currentVersion = GetCurrentVersion();
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PartitionPilot/" + currentVersion);
            client.Timeout = TimeSpan.FromSeconds(10);

            var json = await client.GetStringAsync(LatestReleaseApiUrl);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var htmlUrl = root.GetProperty("html_url").GetString() ?? "";

            var latestVersion = tagName.TrimStart('v', 'V');
            if (IsNewerVersion(latestVersion, currentVersion))
            {
                var verification = EvaluateReleaseAssetVerification(root);
                return (true, latestVersion, htmlUrl, verification.Status, verification.Detail);
            }

            return (false, currentVersion, "", "Current", "No newer release is available.");
        }
        catch
        {
            return null;
        }
    }

    public static string BuildLatestReleaseApiUrl(string repoUrl)
    {
        if (!Uri.TryCreate(repoUrl, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A GitHub repository URL is required.", nameof(repoUrl));
        }

        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            throw new ArgumentException("The GitHub repository URL must include owner and repository name.", nameof(repoUrl));

        return $"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases/latest";
    }

    public static ReleaseVerificationResult EvaluateReleaseAssetVerification(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return new ReleaseVerificationResult("UnsignedLocalTest", "GitHub release contains no downloadable assets.");

        var hasManifest = false;
        var installableAssetName = "";

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (IsManifestAsset(name))
                hasManifest = true;

            if (!IsInstallableAsset(name))
                continue;

            installableAssetName = name;
            var digest = asset.TryGetProperty("digest", out var digestProp) ? digestProp.GetString() ?? "" : "";
            if (IsSha256Digest(digest))
                return new ReleaseVerificationResult("Verified", $"{name} publishes GitHub SHA256 digest {digest}.");
        }

        if (hasManifest)
            return new ReleaseVerificationResult("Manifest", "Release publishes a SHA256 manifest; verify it before manual install.");

        var target = string.IsNullOrWhiteSpace(installableAssetName) ? "release" : installableAssetName;
        return new ReleaseVerificationResult("UnsignedLocalTest", $"{target} has no SHA256 digest or manifest; treat as an unsigned local-test build.");
    }

    private static bool IsInstallableAsset(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private static bool IsManifestAsset(string name) =>
        name.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("SHA256SUMS.json", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase);

    private static bool IsSha256Digest(string digest)
    {
        const string prefix = "sha256:";
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var hash = digest[prefix.Length..];
        return hash.Length == 64 && hash.All(Uri.IsHexDigit);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
