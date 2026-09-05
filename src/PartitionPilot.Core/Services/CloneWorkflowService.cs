namespace PartitionPilot;

public static class CloneWorkflowService
{
    public static IReadOnlyList<WorkflowPrompt> BuildSectorClonePrompts(
        DiskIdentitySnapshot sourceIdentity,
        DiskIdentitySnapshot destinationIdentity)
    {
        return
        [
            new WorkflowPrompt(
                CoreStrings.Get("SectorClone1Title"),
                CoreStrings.Format("SectorClone1Body",
                    sourceIdentity.ConfirmationSummary, destinationIdentity.ConfirmationSummary),
                true),
            new WorkflowPrompt(
                CoreStrings.Get("SectorCloneFinalTitle"),
                CoreStrings.Get("SectorCloneFinalBody"),
                true)
        ];
    }

    /// <summary>
    /// Robocopy arguments for capturing a volume into a mounted VHDX.
    /// <para>
    /// The switches are load-bearing, not decoration. Without <c>/COPYALL</c> every security
    /// descriptor, owner and audit entry is dropped, so a restored image lands with inherited default
    /// permissions. Without <c>/XJ</c> robocopy walks junction points, and a Windows volume root
    /// carries <c>Documents and Settings</c> pointing back into <c>Users</c>. <c>/B</c> reads files
    /// whose ACLs would otherwise deny access, and <c>/DCOPY:DAT</c> keeps directory timestamps.
    /// The excluded entries are per-volume state that cannot be copied or must not be.
    /// </para>
    /// </summary>
    /// <param name="privileged">
    /// When true, request auditing information and backup-mode reads. Both need privileges an elevated
    /// administrator normally holds, and robocopy refuses to copy anything at all when they are missing,
    /// so callers fall back to <c>false</c> rather than failing the capture outright.
    /// </param>
    public static string BuildVhdxCaptureArguments(string captureSource, char destinationLetter, bool privileged = true)
    {
        var excludedDirectories = string.Join(" ", VhdxCaptureExcludedDirectories.Select(d => $"\"{d}\""));
        var excludedFiles = string.Join(" ", VhdxCaptureExcludedFiles.Select(f => $"\"{f}\""));

        // /COPYALL is /COPY:DATSOU — the U is auditing information and needs the Manage Auditing right;
        // /B needs Backup and Restore. Without either, robocopy exits 16 having copied nothing. The
        // reduced form still carries the ACLs and owner that decide whether a restored image is usable.
        var fidelity = privileged ? "/COPYALL /B" : "/COPY:DATSO";

        // A quoted path ending in a single backslash escapes its own closing quote, so the destination
        // would swallow every switch that follows it and robocopy would fail with ERROR 123. The
        // destination must keep its trailing separator to mean "the root of that drive", so the
        // backslash is doubled; the source can simply drop its own.
        return $"\"{captureSource.TrimEnd('\\')}\" \"{char.ToUpperInvariant(destinationLetter)}:\\\\\" " +
               $"/MIR {fidelity} /DCOPY:DAT /XJ /R:0 /W:0 /NP /NDL /NFL " +
               $"/XD {excludedDirectories} /XF {excludedFiles}";
    }

    /// <summary>
    /// True when a robocopy failure is the "you lack the privilege for these switches" refusal, which is
    /// worth retrying at reduced fidelity rather than surfacing as a failed capture.
    /// </summary>
    public static bool IsMissingPrivilegeFailure(string message) =>
        message.Contains("Manage Auditing user right", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Backup and Restore Files user rights", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> VhdxCaptureExcludedDirectories { get; } =
    [
        "System Volume Information",
        "$Recycle.Bin",
        "$RECYCLE.BIN"
    ];

    public static IReadOnlyList<string> VhdxCaptureExcludedFiles { get; } =
    [
        "pagefile.sys",
        "hiberfil.sys",
        "swapfile.sys"
    ];

    public static string BuildCompletionSummary(
        int sourceDiskNumber,
        int destinationDiskNumber,
        string cloneReport,
        string bootAuditReport)
    {
        return CoreStrings.Format("SectorCloneCompleteBody",
            sourceDiskNumber, destinationDiskNumber, cloneReport, bootAuditReport);
    }
}
