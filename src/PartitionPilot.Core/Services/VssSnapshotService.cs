using System.Text.RegularExpressions;

namespace PartitionPilot;

public sealed class VssSnapshot : IAsyncDisposable
{
    private readonly IProcessRunner _runner;
    private readonly IActivityLog _log;
    private bool _disposed;

    public string ShadowCopyId { get; }
    public string ShadowCopyPath { get; }
    public char VolumeLetter { get; }

    internal VssSnapshot(string shadowCopyId, string shadowCopyPath, char volumeLetter,
        IProcessRunner runner, IActivityLog log)
    {
        ShadowCopyId = shadowCopyId;
        ShadowCopyPath = shadowCopyPath.TrimEnd('\\') + '\\';
        VolumeLetter = volumeLetter;
        _runner = runner;
        _log = log;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _log.Log($"Deleting VSS shadow copy {ShadowCopyId}...");
            await _runner.RunExeAsync("vssadmin", $"delete shadows /Shadow={ShadowCopyId} /Quiet", _log);
            _log.Log("VSS shadow copy deleted.");
        }
        catch (Exception ex)
        {
            _log.Log($"VSS shadow copy cleanup failed (manual cleanup may be needed): {ex.Message}");
        }
    }
}

public static class VssSnapshotService
{
    private static readonly Regex ShadowIdPattern = new(
        @"Shadow Copy ID:\s*\{?([0-9a-fA-F-]+)\}?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ShadowPathPattern = new(
        @"Shadow Copy Volume Name:\s*(\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static async Task<VssSnapshot> CreateSnapshotAsync(
        char volumeLetter, IProcessRunner runner, IActivityLog log, CancellationToken ct = default)
    {
        log.Log($"Creating VSS shadow copy for {volumeLetter}:\\...");

        var output = await runner.RunExeAsync("vssadmin",
            $"create shadow /for={volumeLetter}:", log, ct: ct);

        var idMatch = ShadowIdPattern.Match(output);
        var pathMatch = ShadowPathPattern.Match(output);

        if (!idMatch.Success || !pathMatch.Success)
            throw new InvalidOperationException(
                $"VSS snapshot creation succeeded but output could not be parsed. Output: {output.Trim()}");

        var shadowId = $"{{{idMatch.Groups[1].Value}}}";
        var shadowPath = pathMatch.Groups[1].Value;

        log.Log($"VSS shadow copy created: {shadowId} at {shadowPath}");

        return new VssSnapshot(shadowId, shadowPath, volumeLetter, runner, log);
    }

    public static async Task<bool> IsAvailableAsync(IProcessRunner runner, IActivityLog log)
    {
        try
        {
            await runner.RunExeAsync("vssadmin", "list providers", log);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
