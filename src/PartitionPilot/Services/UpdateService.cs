using System.Net.Http;
using System.Text.Json;

namespace PartitionPilot;

public static class UpdateService
{
    private const string CurrentVersion = "0.2.1";
    private const string ReleasesApiUrl = "https://api.github.com/repos/SysAdminDoc/PartitionPilot/releases/latest";

    public static async Task<(bool available, string version, string url)?> CheckForUpdateAsync()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PartitionPilot/" + CurrentVersion);
            client.Timeout = TimeSpan.FromSeconds(10);

            var json = await client.GetStringAsync(ReleasesApiUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var htmlUrl = root.GetProperty("html_url").GetString() ?? "";

            var latestVersion = tagName.TrimStart('v', 'V');
            if (Version.TryParse(latestVersion, out var latest) &&
                Version.TryParse(CurrentVersion, out var current) &&
                latest > current)
                return (true, latestVersion, htmlUrl);

            return (false, CurrentVersion, "");
        }
        catch
        {
            return null;
        }
    }
}
