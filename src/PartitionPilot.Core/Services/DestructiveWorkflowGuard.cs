namespace PartitionPilot;

public static class DestructiveWorkflowGuard
{
    public static bool ConfirmPrompts(IEnumerable<WorkflowPrompt> prompts, IDialogService dialog)
    {
        foreach (var prompt in prompts)
        {
            var confirmed = prompt.IsDanger
                ? dialog.ConfirmDanger(prompt.Message, prompt.Title)
                : dialog.ConfirmWarning(prompt.Message, prompt.Title);
            if (!confirmed)
                return false;
        }

        return true;
    }

    public static async Task<bool> VerifyDiskIdentityBeforeExecuteAsync(
        DiskIdentitySnapshot identity,
        string title,
        IWmiDiskService wmiService,
        IActivityLog log,
        IDialogService dialog)
    {
        try
        {
            await identity.VerifyCurrentAsync(wmiService);
            return true;
        }
        catch (Exception ex)
        {
            log.Log($"Target identity check failed: {ex.Message}");
            dialog.ShowError(ex.Message, title);
            return false;
        }
    }
}
