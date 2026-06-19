using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace PartitionPilot;

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

    public static async Task<UpdateInfo?> CheckForVelopackUpdateAsync(ActivityLog? log = null)
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

    public static async Task DownloadAndApplyAsync(UpdateInfo update, ActivityLog? log = null)
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
            log?.Log($"Downloading update v{update.TargetFullRelease.Version}...");
            await mgr.DownloadUpdatesAsync(update);
            log?.Log("Update downloaded. Applying on next restart.");
        }
        catch (Exception ex)
        {
            log?.Log($"Update download failed: {ex.Message}");
            throw;
        }
    }

    public static void ApplyAndRestart(UpdateInfo update, ActivityLog? log = null)
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

    public static async Task<(bool available, string version, string url)?> CheckForUpdateAsync()
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
                return (true, latestVersion, htmlUrl);

            return (false, currentVersion, "");
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
}
