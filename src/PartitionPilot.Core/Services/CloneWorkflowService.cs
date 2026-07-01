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

    public static string BuildCompletionSummary(
        int sourceDiskNumber,
        int destinationDiskNumber,
        string cloneReport,
        string bootAuditReport)
    {
        return $"Sector clone complete.\n\nDisk {sourceDiskNumber} -> Disk {destinationDiskNumber}\n{cloneReport}\n\n{bootAuditReport}";
    }
}
