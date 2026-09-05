namespace PartitionPilot;

public sealed record WorkflowPrompt(string Title, string Message, bool IsDanger);

public static class WipeWorkflowService
{
    public static WorkflowPrompt BuildFreeSpacePrompt(char letter, string? encryptionStatus)
    {
        var encryptionLine = string.IsNullOrWhiteSpace(encryptionStatus)
            ? ""
            : CoreStrings.Format("WipeEncryptionLine", encryptionStatus);

        return new WorkflowPrompt(
            CoreStrings.Get("WipeFreeSpaceTitle"),
            CoreStrings.Format("WipeFreeSpaceBody", char.ToUpperInvariant(letter), encryptionLine),
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
                CoreStrings.Get("WipeDisk1Title"),
                CoreStrings.Format("WipeDisk1Body", diskIdentity.ConfirmationSummary),
                false),
            new WorkflowPrompt(
                CoreStrings.Get("WipeDisk2Title"),
                CoreStrings.Format("WipeDisk2Body",
                    diskIdentity.DiskNumber, diskIdentity.ConfirmationSummary,
                    SizeUtil.Format(diskSize), wipeMode),
                false),
            new WorkflowPrompt(
                CoreStrings.Get("WipeDiskFinalTitle"),
                CoreStrings.Get("WipeDiskFinalBody"),
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
                CoreStrings.Format("DodWipe1Title", passCount),
                CoreStrings.Format("DodWipe1Body",
                    passCount, diskIdentity.ConfirmationSummary, SizeUtil.Format(diskSize)),
                false),
            new WorkflowPrompt(
                CoreStrings.Format("DodWipeFinalTitle", passCount),
                CoreStrings.Format("DodWipeFinalBody", passCount),
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
                CoreStrings.Get("NvmeSanitize1Title"),
                CoreStrings.Format("NvmeSanitize1Body", diskIdentity.ConfirmationSummary, method),
                true),
            new WorkflowPrompt(
                CoreStrings.Get("NvmeSanitizeFinalTitle"),
                CoreStrings.Get("NvmeSanitizeFinalBody"),
                true)
        ];
    }
}
