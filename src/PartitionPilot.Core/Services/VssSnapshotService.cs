using System.Management;
using System.Text.RegularExpressions;

namespace PartitionPilot;

/// <summary>
/// Result of a successful <c>Win32_ShadowCopy.Create</c> call: the shadow copy's identifier and
/// the <c>\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopyN</c> device path it can be read through.
/// </summary>
public sealed record ShadowCopyCreateResult(string ShadowCopyId, string DeviceObject);

/// <summary>
/// Creates and deletes volume shadow copies. Injected so the capture path can be exercised in tests
/// without touching the machine's real VSS service.
/// </summary>
public interface IShadowCopyProvider
{
    Task<ShadowCopyCreateResult> CreateAsync(char volumeLetter, CancellationToken ct = default);
    Task DeleteAsync(string shadowCopyId, CancellationToken ct = default);
}

/// <summary>
/// Creates shadow copies through the <c>Win32_ShadowCopy</c> WMI class in <c>root\CIMV2</c>.
/// <para>
/// This is deliberately not <c>vssadmin create shadow</c>: that verb ships only on Windows Server
/// SKUs, so on Windows 10/11 client — the only platform PartitionPilot supports — it never succeeds.
/// </para>
/// </summary>
public sealed class WmiShadowCopyProvider : IShadowCopyProvider
{
    private const string CimScope = @"\\.\root\CIMV2";

    private static readonly Dictionary<uint, string> CreateReturnCodes = new()
    {
        [1] = "Access denied — shadow copy creation requires an elevated session.",
        [2] = "Invalid argument.",
        [3] = "Specified volume not found.",
        [4] = "Specified volume not supported — shadow copies require a local NTFS volume.",
        [5] = "Unsupported shadow copy context.",
        [6] = "Insufficient storage — increase the shadow copy storage association with 'vssadmin resize shadowstorage'.",
        [7] = "Volume is in use.",
        [8] = "Maximum number of shadow copies reached.",
        [9] = "Another shadow copy operation is already in progress.",
        [10] = "Shadow copy provider vetoed the operation.",
        [11] = "Shadow copy provider not registered.",
        [12] = "Shadow copy provider failure.",
        [13] = "Unknown error."
    };

    public Task<ShadowCopyCreateResult> CreateAsync(char volumeLetter, CancellationToken ct = default) =>
        Task.Run(() => Create(volumeLetter), ct);

    public Task DeleteAsync(string shadowCopyId, CancellationToken ct = default) =>
        Task.Run(() => Delete(shadowCopyId), ct);

    private static ShadowCopyCreateResult Create(char volumeLetter)
    {
        using var shadowCopyClass = new ManagementClass(
            new ManagementScope(CimScope), new ManagementPath("Win32_ShadowCopy"), null);

        using var inParams = shadowCopyClass.GetMethodParameters("Create");
        inParams["Volume"] = $"{char.ToUpperInvariant(volumeLetter)}:\\";
        inParams["Context"] = "ClientAccessible";

        using var outParams = shadowCopyClass.InvokeMethod("Create", inParams, null);
        var returnValue = Convert.ToUInt32(outParams["ReturnValue"]);
        if (returnValue != 0)
            throw new InvalidOperationException(
                $"Win32_ShadowCopy.Create returned {returnValue}: {DescribeCreateReturnCode(returnValue)}");

        var shadowId = outParams["ShadowID"] as string;
        if (string.IsNullOrWhiteSpace(shadowId))
            throw new InvalidOperationException("Win32_ShadowCopy.Create succeeded but returned no ShadowID.");

        return new ShadowCopyCreateResult(shadowId, ResolveDeviceObject(shadowId));
    }

    private static string ResolveDeviceObject(string shadowId)
    {
        using var searcher = new ManagementObjectSearcher(
            new ManagementScope(CimScope),
            new ObjectQuery($"SELECT DeviceObject FROM Win32_ShadowCopy WHERE ID = {WqlStringLiteral(shadowId)}"));

        using var results = searcher.Get();
        foreach (ManagementObject obj in results)
        {
            using (obj)
            {
                var device = obj["DeviceObject"] as string;
                if (!string.IsNullOrWhiteSpace(device))
                    return device;
            }
        }

        throw new InvalidOperationException(
            $"Shadow copy {shadowId} was created but no DeviceObject path could be read back for it.");
    }

    private static void Delete(string shadowCopyId)
    {
        using var searcher = new ManagementObjectSearcher(
            new ManagementScope(CimScope),
            new ObjectQuery($"SELECT * FROM Win32_ShadowCopy WHERE ID = {WqlStringLiteral(shadowCopyId)}"));

        using var results = searcher.Get();
        foreach (ManagementObject obj in results)
        {
            using (obj)
                obj.Delete();
        }
    }

    internal static string DescribeCreateReturnCode(uint returnValue) =>
        CreateReturnCodes.TryGetValue(returnValue, out var description)
            ? description
            : $"Undocumented return code {returnValue}.";

    private static string WqlStringLiteral(string value) =>
        "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
                   .Replace("'", "\\'", StringComparison.Ordinal) + "'";
}

public sealed class VssSnapshot : IAsyncDisposable
{
    private readonly IShadowCopyProvider _provider;
    private readonly IActivityLog _log;
    private bool _disposed;

    public string ShadowCopyId { get; }
    public string ShadowCopyPath { get; }
    public char VolumeLetter { get; }

    internal VssSnapshot(string shadowCopyId, string shadowCopyPath, char volumeLetter,
        IShadowCopyProvider provider, IActivityLog log)
    {
        ShadowCopyId = shadowCopyId;
        ShadowCopyPath = shadowCopyPath.TrimEnd('\\') + '\\';
        VolumeLetter = volumeLetter;
        _provider = provider;
        _log = log;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _log.Log($"Deleting VSS shadow copy {ShadowCopyId}...");
            await _provider.DeleteAsync(ShadowCopyId);
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
        char volumeLetter, IShadowCopyProvider provider, IActivityLog log, CancellationToken ct = default)
    {
        log.Log($"Creating VSS shadow copy for {volumeLetter}:\\...");

        var result = await provider.CreateAsync(volumeLetter, ct);

        if (string.IsNullOrWhiteSpace(result.ShadowCopyId) || string.IsNullOrWhiteSpace(result.DeviceObject))
            throw new InvalidOperationException(
                "VSS snapshot creation reported success but returned no shadow copy identifier or device path.");

        log.Log($"VSS shadow copy created: {result.ShadowCopyId} at {result.DeviceObject}");

        return new VssSnapshot(result.ShadowCopyId, result.DeviceObject, volumeLetter, provider, log);
    }

    /// <summary>
    /// Proves that a shadow copy can actually be created and deleted on <paramref name="volumeLetter"/>.
    /// A probe that only lists providers or writers passes on client Windows even when creation is
    /// impossible, so it is not a substitute for this.
    /// </summary>
    public static async Task<VssCreationProbeResult> ProbeCreationAsync(
        char volumeLetter, IShadowCopyProvider provider, IActivityLog log, CancellationToken ct = default)
    {
        VssSnapshot snapshot;
        try
        {
            snapshot = await CreateSnapshotAsync(volumeLetter, provider, log, ct);
        }
        catch (Exception ex)
        {
            return new VssCreationProbeResult(false, ex.Message,
                "Run elevated, confirm the Volume Shadow Copy service is running, and ensure the volume is local NTFS " +
                "with shadow copy storage available ('vssadmin list shadowstorage').");
        }

        // Deleted through the provider rather than DisposeAsync: disposal swallows cleanup failures by
        // design, and a probe that reports success while leaving an orphaned shadow copy behind is worse
        // than one that reports nothing.
        try
        {
            await provider.DeleteAsync(snapshot.ShadowCopyId, ct);
        }
        catch (Exception ex)
        {
            return new VssCreationProbeResult(false,
                $"Created shadow copy {snapshot.ShadowCopyId} but could not delete it: {ex.Message}",
                $"Remove it manually with 'vssadmin delete shadows /Shadow={snapshot.ShadowCopyId}' before " +
                "running diagnostics again, or shadow copies will accumulate on the volume.");
        }

        return new VssCreationProbeResult(true,
            $"Created and removed a test shadow copy on {char.ToUpperInvariant(volumeLetter)}:.", "");
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

public sealed record VssCreationProbeResult(bool CanCreate, string Detail, string Remediation);

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
