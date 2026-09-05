using System.Globalization;
using System.Text.RegularExpressions;

namespace PartitionPilot;

/// <summary>What is holding a shrink back, and what the operator can do about it.</summary>
public sealed record ShrinkBlocker(
    string FilePath,
    long LastClusterOfFile,
    long ShrinkTargetLcn,
    string NtfsFileFlags,
    string VolumeGuidPath,
    ShrinkBlockerKind Kind)
{
    /// <summary>
    /// Clusters between where the shrink wanted to stop and where the blocking file actually ends.
    /// This is the span that becomes reclaimable once the blocker is dealt with.
    /// </summary>
    public long BlockedClusters => Math.Max(0, LastClusterOfFile - ShrinkTargetLcn);

    public long BlockedBytes(int bytesPerCluster) => BlockedClusters * Math.Max(1, bytesPerCluster);

    /// <summary>The command that resolves the cluster number back to a file, straight from the event.</summary>
    public string QueryClusterCommand =>
        $"fsutil volume querycluster {VolumeGuidPath} 0x{LastClusterOfFile:x}";

    public string Remedy => Kind switch
    {
        ShrinkBlockerKind.ShadowCopyStorage =>
            "Delete or shrink the shadow copy storage: 'vssadmin delete shadows /for=C: /all', " +
            "or cap it with 'vssadmin resize shadowstorage /for=C: /on=C: /maxsize=5%'. " +
            "This removes System Restore points and previous versions.",
        ShrinkBlockerKind.PageFile =>
            "Move or disable the page file from System Properties > Advanced > Performance > Virtual memory, " +
            "restart, shrink, then restore it.",
        ShrinkBlockerKind.HibernationFile =>
            "Turn hibernation off with 'powercfg /hibernate off', shrink, then turn it back on. " +
            "This also removes Fast Startup until it is re-enabled.",
        ShrinkBlockerKind.UsnJournal =>
            "Delete the NTFS change journal with 'fsutil usn deletejournal /d C:'. " +
            "Windows recreates it; indexing and backup tools will rebuild their state.",
        ShrinkBlockerKind.SearchIndex =>
            "Stop the Windows Search service and move or rebuild the index from " +
            "Indexing Options > Advanced, then shrink.",
        ShrinkBlockerKind.NtfsMetadata =>
            "This is NTFS metadata, which cannot be moved while the volume is mounted. " +
            "Shrink from an offline environment such as the PartitionPilot rescue media, or accept the limit.",
        _ =>
            "Identify the file with the fsutil command above, move or delete it if it is not system-owned, " +
            "then retry the shrink."
    };
}

public enum ShrinkBlockerKind
{
    Unknown,
    ShadowCopyStorage,
    PageFile,
    HibernationFile,
    UsnJournal,
    SearchIndex,
    NtfsMetadata
}

/// <summary>
/// Explains why Windows will not shrink a volume past a certain point.
/// <para>
/// "You cannot shrink a volume beyond the point where any unmovable files are located" is the most common
/// complaint about Windows partitioning, and Windows already writes the answer to the Application log and
/// never shows it: event 259 from Microsoft-Windows-Defrag names the blocking file, its last cluster, the
/// shrink target, and the fsutil command that resolves the cluster. Reading it needs no elevation.
/// </para>
/// </summary>
public static class ShrinkBlockerService
{
    private static readonly Regex FilePattern = new(
        @"The last unmovable file appears to be:\s*(?<path>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex LastClusterPattern = new(
        @"The last cluster of the file is:\s*0x(?<value>[0-9a-fA-F]+)",
        RegexOptions.Compiled);

    private static readonly Regex ShrinkTargetPattern = new(
        @"Shrink potential target \(LCN address\):\s*0x(?<value>[0-9a-fA-F]+)",
        RegexOptions.Compiled);

    private static readonly Regex FlagsPattern = new(
        @"The NTFS file flags are:\s*(?<flags>\S+)",
        RegexOptions.Compiled);

    private static readonly Regex VolumeGuidPattern = new(
        @"(?<volume>\\\\\?\\Volume\{[0-9a-fA-F-]+\})",
        RegexOptions.Compiled);

    private static readonly Regex VolumeLetterPattern = new(
        @"volume\s*(?:\([^)]*\((?<letter>[A-Za-z]):\)|\((?<letter2>[A-Za-z]):\))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>The GUID Windows uses for shadow copy storage inside System Volume Information.</summary>
    private const string ShadowCopyStorageGuid = "3808876b-c176-4e48-b7ae-04046e6cc752";

    /// <summary>
    /// Parses one Microsoft-Windows-Defrag event 259 message. Returns null when the text is not a shrink
    /// analysis record or is missing the fields that make it actionable.
    /// </summary>
    public static ShrinkBlocker? ParseEvent259(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var file = FilePattern.Match(message);
        var lastCluster = LastClusterPattern.Match(message);
        var shrinkTarget = ShrinkTargetPattern.Match(message);
        if (!file.Success || !lastCluster.Success || !shrinkTarget.Success)
            return null;

        if (!TryParseHex(lastCluster.Groups["value"].Value, out var lastClusterValue) ||
            !TryParseHex(shrinkTarget.Groups["value"].Value, out var shrinkTargetValue))
            return null;

        var path = file.Groups["path"].Value.Trim();

        return new ShrinkBlocker(
            path,
            lastClusterValue,
            shrinkTargetValue,
            FlagsPattern.Match(message) is { Success: true } flags ? flags.Groups["flags"].Value : "",
            VolumeGuidPattern.Match(message) is { Success: true } volume ? volume.Groups["volume"].Value : "",
            Classify(path));
    }

    /// <summary>Reads the drive letter the analysis ran against, so a report can be matched to a volume.</summary>
    public static char? ParseVolumeLetter(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var match = VolumeLetterPattern.Match(message);
        if (!match.Success)
            return null;

        var letter = match.Groups["letter"].Success ? match.Groups["letter"].Value : match.Groups["letter2"].Value;
        return string.IsNullOrEmpty(letter) ? null : char.ToUpperInvariant(letter[0]);
    }

    internal static ShrinkBlockerKind Classify(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ShrinkBlockerKind.Unknown;

        if (path.Contains(ShadowCopyStorageGuid, StringComparison.OrdinalIgnoreCase))
            return ShrinkBlockerKind.ShadowCopyStorage;

        if (path.Contains("$UsnJrnl", StringComparison.OrdinalIgnoreCase))
            return ShrinkBlockerKind.UsnJournal;

        if (path.Contains("pagefile.sys", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("swapfile.sys", StringComparison.OrdinalIgnoreCase))
            return ShrinkBlockerKind.PageFile;

        if (path.Contains("hiberfil.sys", StringComparison.OrdinalIgnoreCase))
            return ShrinkBlockerKind.HibernationFile;

        if (path.Contains("Windows.edb", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"Search\Data", StringComparison.OrdinalIgnoreCase))
            return ShrinkBlockerKind.SearchIndex;

        // Shadow copies live under System Volume Information too, but so does other per-volume state,
        // so the GUID check above is what distinguishes them; anything else there is metadata.
        if (path.Contains("System Volume Information", StringComparison.OrdinalIgnoreCase))
            return ShrinkBlockerKind.NtfsMetadata;

        // NTFS metadata files all start with $ at the volume root or under $Extend.
        var trimmed = path.TrimStart('\\');
        if (trimmed.StartsWith('$'))
            return ShrinkBlockerKind.NtfsMetadata;

        return ShrinkBlockerKind.Unknown;
    }

    public static string FormatReport(ShrinkBlocker blocker, int bytesPerCluster, long? currentMinimumSize = null)
    {
        ArgumentNullException.ThrowIfNull(blocker);

        var lines = new List<string>
        {
            $"Blocking file: {blocker.FilePath}",
            $"Kind:          {Describe(blocker.Kind)}",
            $"Blocked span:  {SizeUtil.Format(blocker.BlockedBytes(bytesPerCluster))} " +
            $"({blocker.BlockedClusters:N0} cluster(s) at {SizeUtil.Format(bytesPerCluster)} each)"
        };

        if (currentMinimumSize is > 0)
            lines.Add($"Current floor: shrink cannot go below {SizeUtil.Format(currentMinimumSize.Value)}");

        if (!string.IsNullOrWhiteSpace(blocker.NtfsFileFlags))
            lines.Add($"NTFS flags:    {blocker.NtfsFileFlags}");

        lines.Add("");
        lines.Add($"Remedy: {blocker.Remedy}");

        if (!string.IsNullOrWhiteSpace(blocker.VolumeGuidPath))
        {
            lines.Add("");
            lines.Add($"Confirm the file with: {blocker.QueryClusterCommand}");
            lines.Add("That needs an elevated shell, and PowerShell needs the volume path quoted because of the braces.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Finds the most recent shrink analysis Windows recorded for <paramref name="driveLetter"/>.
    /// Reading the Application log needs no elevation.
    /// </summary>
    public static async Task<ShrinkBlocker?> FindLatestBlockerAsync(
        char driveLetter, IProcessRunner runner, IActivityLog? log = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runner);

        var letter = char.ToUpperInvariant(driveLetter);
        if (letter is < 'A' or > 'Z')
            throw new ArgumentException("Drive letter must be A-Z.", nameof(driveLetter));

        const string command =
            "Get-WinEvent -FilterHashtable @{LogName='Application';ProviderName='Microsoft-Windows-Defrag';Id=259} " +
            "-MaxEvents 40 -ErrorAction Stop | ForEach-Object { \"===EVENT===\"; $_.Message }";

        string output;
        try
        {
            output = await runner.RunPowerShellAsync(command, log, ct);
        }
        catch (Exception ex)
        {
            log?.Log($"No shrink analysis records could be read: {ex.Message}");
            return null;
        }

        foreach (var record in output.Split("===EVENT===", StringSplitOptions.RemoveEmptyEntries))
        {
            if (ParseVolumeLetter(record) != letter)
                continue;

            var blocker = ParseEvent259(record);
            if (blocker is not null)
                return blocker;
        }

        return null;
    }

    internal static string Describe(ShrinkBlockerKind kind) => kind switch
    {
        ShrinkBlockerKind.ShadowCopyStorage => "Shadow copy storage (System Restore / previous versions)",
        ShrinkBlockerKind.PageFile => "Page file",
        ShrinkBlockerKind.HibernationFile => "Hibernation file",
        ShrinkBlockerKind.UsnJournal => "NTFS change journal",
        ShrinkBlockerKind.SearchIndex => "Windows Search index",
        ShrinkBlockerKind.NtfsMetadata => "NTFS metadata (not movable while mounted)",
        _ => "Unrecognised file"
    };

    private static bool TryParseHex(string value, out long parsed) =>
        long.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
}
