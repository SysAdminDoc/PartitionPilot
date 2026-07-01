namespace PartitionPilot;

public sealed record WorkflowPrompt(string Title, string Message, bool IsDanger);

public static class WipeWorkflowService
{
    public static WorkflowPrompt BuildFreeSpacePrompt(char letter, string? encryptionStatus)
    {
        var encryptionLine = string.IsNullOrWhiteSpace(encryptionStatus) ? "" : $"\nEncryption: {encryptionStatus}\n";
        return new WorkflowPrompt(
            "Confirm Free-Space Wipe",
            $"Wipe free space on {char.ToUpperInvariant(letter)}:?\n{encryptionLine}\nExisting files remain in place. Previously deleted data in free space will be overwritten.",
            false);
    }

    public static IReadOnlyList<WorkflowPrompt> BuildFullDiskPrompts(
        DiskIdentitySnapshot diskIdentity,
        string wipeMode,
        long diskSize)
    {
        return
        [
            new WorkflowPrompt(
                "Wipe Disk -- Confirmation 1 of 3",
                $"WARNING: You are about to wipe:\n{diskIdentity.ConfirmationSummary}\n\nALL DATA ON THIS DISK WILL BE PERMANENTLY DESTROYED.\n\nContinue?",
                false),
            new WorkflowPrompt(
                "Wipe Disk -- Confirmation 2 of 3",
                $"Are you absolutely sure you want to wipe Disk {diskIdentity.DiskNumber}?\n\nTarget:\n{diskIdentity.ConfirmationSummary}\nSize: {SizeUtil.Format(diskSize)}\nMode: {wipeMode}\n\nThis CANNOT be undone.",
                false),
            new WorkflowPrompt(
                "Wipe Disk -- FINAL Confirmation",
                "FINAL WARNING: Click Yes to begin disk wipe immediately.",
                true)
        ];
    }

    public static IReadOnlyList<WorkflowPrompt> BuildDodPrompts(
        DiskIdentitySnapshot diskIdentity,
        int passCount,
        long diskSize)
    {
        return
        [
            new WorkflowPrompt(
                $"DoD {passCount}-Pass Wipe -- Confirmation 1 of 2",
                $"DoD 5220.22-M {passCount}-PASS WIPE target:\n{diskIdentity.ConfirmationSummary}\n\nSize: {SizeUtil.Format(diskSize)}\n\nALL DATA WILL BE PERMANENTLY DESTROYED with multiple overwrite passes.\n\nContinue?",
                false),
            new WorkflowPrompt(
                $"DoD {passCount}-Pass Wipe -- FINAL Confirmation",
                $"FINAL WARNING: {passCount}-pass wipe will write the entire disk {passCount} times. This may take hours on large drives. Click Yes to begin.",
                true)
        ];
    }

    public static IReadOnlyList<WorkflowPrompt> BuildNvmeSanitizePrompts(
        DiskIdentitySnapshot diskIdentity,
        SecureEraseService.SanitizeMethod method)
    {
        return
        [
            new WorkflowPrompt(
                "NVMe Sanitize -- Confirmation 1 of 2",
                $"NVMe FIRMWARE ERASE target:\n{diskIdentity.ConfirmationSummary}\n\nMethod: {method}\n\nThis sends a firmware-level sanitize command directly to the drive controller. ALL DATA WILL BE PERMANENTLY AND IRREVERSIBLY DESTROYED.\n\nThis operation cannot be cancelled once started.",
                true),
            new WorkflowPrompt(
                "NVMe Sanitize -- FINAL Confirmation",
                "FINAL WARNING: NVMe sanitize is a hardware-level operation that erases ALL data including data in over-provisioned and remapped sectors.\n\nProceed?",
                true)
        ];
    }
}
