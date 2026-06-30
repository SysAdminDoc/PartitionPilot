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

    private static readonly Regex WriterNamePattern = new(
        @"^\s*Writer name:\s*'(?<name>[^']+)'",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WriterIdPattern = new(
        @"^\s*Writer Id:\s*(?<id>\{?[0-9a-fA-F-]+\}?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WriterInstancePattern = new(
        @"^\s*Writer Instance Id:\s*(?<id>\{?[0-9a-fA-F-]+\}?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WriterStatePattern = new(
        @"^\s*State:\s*(?<state>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WriterLastErrorPattern = new(
        @"^\s*Last error:\s*(?<error>.+)$",
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

    public static async Task<VssWriterHealthReport> CheckWriterHealthAsync(
        IProcessRunner runner,
        IActivityLog log,
        CancellationToken ct = default)
    {
        var output = await runner.RunExeAsync("vssadmin", "list writers", log, ignoreStderrOnSuccess: true, ct: ct);
        var report = ParseWriterHealth(output);
        log.Log(report.IsHealthy
            ? $"VSS writer health OK: {report.Summary}"
            : $"VSS writer health failed: {report.Summary}");
        return report;
    }

    public static async Task<VssWriterHealthReport> EnsureWritersHealthyAsync(
        IProcessRunner runner,
        IActivityLog log,
        CancellationToken ct = default)
    {
        var report = await CheckWriterHealthAsync(runner, log, ct);
        if (!report.IsHealthy)
            throw new InvalidOperationException($"VSS writer health preflight failed: {report.Summary}");

        return report;
    }

    public static VssWriterHealthReport ParseWriterHealth(string output)
    {
        var writers = new List<VssWriterStatus>();
        WriterBuilder? current = null;

        foreach (var rawLine in output.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            var nameMatch = WriterNamePattern.Match(line);
            if (nameMatch.Success)
            {
                AddCurrent();
                current = new WriterBuilder { Name = nameMatch.Groups["name"].Value.Trim() };
                continue;
            }

            if (current is null)
                continue;

            var idMatch = WriterIdPattern.Match(line);
            if (idMatch.Success)
            {
                current.WriterId = idMatch.Groups["id"].Value.Trim();
                continue;
            }

            var instanceMatch = WriterInstancePattern.Match(line);
            if (instanceMatch.Success)
            {
                current.InstanceId = instanceMatch.Groups["id"].Value.Trim();
                continue;
            }

            var stateMatch = WriterStatePattern.Match(line);
            if (stateMatch.Success)
            {
                current.State = stateMatch.Groups["state"].Value.Trim();
                continue;
            }

            var errorMatch = WriterLastErrorPattern.Match(line);
            if (errorMatch.Success)
                current.LastError = errorMatch.Groups["error"].Value.Trim();
        }

        AddCurrent();
        return new VssWriterHealthReport(writers);

        void AddCurrent()
        {
            if (current is null)
                return;

            writers.Add(new VssWriterStatus(
                current.Name,
                current.WriterId,
                current.InstanceId,
                current.State,
                current.LastError));
            current = null;
        }
    }

    private sealed class WriterBuilder
    {
        public string Name { get; set; } = "";
        public string WriterId { get; set; } = "";
        public string InstanceId { get; set; } = "";
        public string State { get; set; } = "";
        public string LastError { get; set; } = "";
    }
}

public sealed record VssWriterStatus(
    string Name,
    string WriterId,
    string InstanceId,
    string State,
    string LastError)
{
    public bool IsHealthy =>
        State.Contains("Stable", StringComparison.OrdinalIgnoreCase) &&
        (string.IsNullOrWhiteSpace(LastError) ||
         LastError.Equals("No error", StringComparison.OrdinalIgnoreCase));

    public string Summary => IsHealthy
        ? $"{Name}: {State}, {LastErrorOrUnknown}"
        : $"{Name}: {StateOrUnknown}, {LastErrorOrUnknown}";

    private string StateOrUnknown => string.IsNullOrWhiteSpace(State) ? "state unknown" : State;
    private string LastErrorOrUnknown => string.IsNullOrWhiteSpace(LastError) ? "last error unknown" : LastError;
}

public sealed class VssWriterHealthReport
{
    public VssWriterHealthReport(IReadOnlyList<VssWriterStatus> writers)
    {
        Writers = writers;
    }

    public IReadOnlyList<VssWriterStatus> Writers { get; }
    public IReadOnlyList<VssWriterStatus> UnhealthyWriters => Writers.Where(w => !w.IsHealthy).ToList();
    public bool HasWriters => Writers.Count > 0;
    public bool IsHealthy => HasWriters && UnhealthyWriters.Count == 0;

    public string Summary
    {
        get
        {
            if (!HasWriters)
                return "No VSS writers were reported.";
            if (IsHealthy)
                return $"{Writers.Count} writer(s) stable with no errors.";

            return string.Join("; ", UnhealthyWriters.Select(w => w.Summary));
        }
    }
}
