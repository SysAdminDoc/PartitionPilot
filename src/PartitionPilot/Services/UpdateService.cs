using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace PartitionPilot;

public static class UpdateService
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/SysAdminDoc/PartitionPilot/releases/latest";

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

    public static async Task<(bool available, string version, string url)?> CheckForUpdateAsync()
    {
        try
        {
            var currentVersion = GetCurrentVersion();
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PartitionPilot/" + currentVersion);
            client.Timeout = TimeSpan.FromSeconds(10);

            var json = await client.GetStringAsync(ReleasesApiUrl);
            using var doc = JsonDocument.Parse(json);
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
}
