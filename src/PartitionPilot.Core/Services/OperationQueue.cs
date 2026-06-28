using System.Collections.ObjectModel;

namespace PartitionPilot;

public enum PendingOperationType
{
    Create,
    Delete,
    Format,
    Resize,
    Extend,
    Split,
    ChangeLetter,
    SetActive,
    HideUnhide,
    Initialize
}

public class PendingOperation
{
    public PendingOperationType Type { get; init; }
    public string Description { get; init; } = "";
    public string DiskTarget { get; init; } = "";
    public DiskIdentitySnapshot? DiskIdentity { get; init; }
    public string RiskLevel { get; init; } = "Normal";
    public Func<Task> ValidateTarget { get; init; } = () => Task.CompletedTask;
    public Func<Task> Execute { get; init; } = () => Task.CompletedTask;

    public string TypeDisplay => Type.ToString();
    public string RiskDisplay => RiskLevel;
}

public class OperationQueue
{
    public ObservableCollection<PendingOperation> Pending { get; } = new();

    public bool HasPending => Pending.Count > 0;

    public int Count => Pending.Count;

    public string SummaryText => Pending.Count switch
    {
        0 => "No pending operations",
        1 => "1 pending operation",
        _ => $"{Pending.Count} pending operations"
    };

    public void Enqueue(PendingOperation op)
    {
        Pending.Add(op);
    }

    public void Remove(PendingOperation op)
    {
        Pending.Remove(op);
    }

    public void Clear()
    {
        Pending.Clear();
    }

    public async Task ApplyAllAsync(IActivityLog log, IDialogService dialog, Action<bool> setBusy, Action<string?> setStatus)
    {
        if (Pending.Count == 0) return;

        setBusy(true);
        var completed = 0;
        var failed = 0;
        var total = Pending.Count;
        var snapshot = Pending.ToList();
        var completedOperations = new List<PendingOperation>();

        var journal = OperationJournalService.CreateJournal(snapshot);
        try { await OperationJournalService.SaveAsync(journal); }
        catch (Exception ex) { log.Log($"Journal write failed (non-fatal): {ex.Message}"); }

        try
        {
            for (int i = 0; i < snapshot.Count; i++)
            {
                var op = snapshot[i];
                setStatus($"Applying {i + 1}/{total}: {op.Description}...");
                log.Log($"Applying operation {i + 1}/{total}: {op.Description}");

                OperationJournalService.UpdateEntry(journal, i, JournalEntryStatus.Applying);
                try { await OperationJournalService.SaveAsync(journal); }
                catch (Exception ex) { log.Log($"Journal write failed (non-fatal): {ex.Message}"); }

                try
                {
                    await op.ValidateTarget();
                    await op.Execute();
                    completed++;
                    completedOperations.Add(op);
                    log.Log($"Operation {i + 1} completed: {op.Description}");

                    OperationJournalService.UpdateEntry(journal, i, JournalEntryStatus.Completed);
                    try { await OperationJournalService.SaveAsync(journal); }
                    catch (Exception ex) { log.Log($"Journal write failed (non-fatal): {ex.Message}"); }
                }
                catch (Exception ex)
                {
                    failed++;
                    log.Log($"Operation {i + 1} failed: {op.Description} - {ex.Message}");

                    OperationJournalService.UpdateEntry(journal, i, JournalEntryStatus.Failed, ex.Message);
                    for (int j = i + 1; j < snapshot.Count; j++)
                        OperationJournalService.UpdateEntry(journal, j, JournalEntryStatus.Skipped);
                    try { await OperationJournalService.SaveAsync(journal); }
                    catch (Exception jex) { log.Log($"Journal write failed (non-fatal): {jex.Message}"); }

                    dialog.ShowError(
                        $"Operation failed: {op.Description}\n\n{ex.Message}\n\n" +
                        $"Completed: {completed}/{total}, Failed: {failed}. Failed and skipped operations remain in the pending queue.",
                        "Operation Failed");
                    break;
                }
            }

            foreach (var op in completedOperations)
                Pending.Remove(op);

            OperationJournalService.MarkCompleted(journal);
            try { await OperationJournalService.SaveAsync(journal); }
            catch (Exception ex) { log.Log($"Journal write failed (non-fatal): {ex.Message}"); }

            if (failed == 0)
            {
                log.Log($"All {completed} operation(s) applied successfully.");
                dialog.ShowInfo($"All {completed} operation(s) applied successfully.", "Operations Complete");
            }
            else
            {
                log.Log($"{Pending.Count} pending operation(s) preserved after failure.");
            }
        }
        finally
        {
            setStatus(null);
            setBusy(false);
            OperationJournalService.PurgeOldJournals();
        }
    }
}
