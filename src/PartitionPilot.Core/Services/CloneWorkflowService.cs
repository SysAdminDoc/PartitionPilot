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
                "Confirm Sector Clone",
                $"WARNING: This will overwrite ALL data on the destination disk with a sector-by-sector copy.\n\nSource:\n{sourceIdentity.ConfirmationSummary}\n\nDestination:\n{destinationIdentity.ConfirmationSummary}\n\nThis operation cannot be undone. Continue?",
                true),
            new WorkflowPrompt(
                "Confirm Clone",
                "FINAL CONFIRMATION: All data on the destination disk will be permanently overwritten with a raw sector copy.",
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
    public static string BuildVhdxCaptureArguments(string captureSource, char destinationLetter)
    {
        var excludedDirectories = string.Join(" ", VhdxCaptureExcludedDirectories.Select(d => $"\"{d}\""));
        var excludedFiles = string.Join(" ", VhdxCaptureExcludedFiles.Select(f => $"\"{f}\""));

        return $"\"{captureSource.TrimEnd('\\')}\" \"{char.ToUpperInvariant(destinationLetter)}:\\\" " +
               $"/MIR /COPYALL /DCOPY:DAT /XJ /B /R:0 /W:0 /NP /NDL /NFL " +
               $"/XD {excludedDirectories} /XF {excludedFiles}";
    }

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
        return $"Sector clone complete.\n\nDisk {sourceDiskNumber} -> Disk {destinationDiskNumber}\n{cloneReport}\n\n{bootAuditReport}";
    }
}
