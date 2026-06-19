namespace PartitionPilot.Tests;

public class OperationQueueTests
{
    [Fact]
    public async Task ApplyAllAsync_WhenOperationFails_PreservesFailedAndSkippedOperations()
    {
        var queue = new OperationQueue();
        var log = new ActivityLog();
        var dialog = new CapturingDialogService();
        var completed = new PendingOperation
        {
            Description = "Completed operation",
            Execute = () => Task.CompletedTask
        };
        var failed = new PendingOperation
        {
            Description = "Failed operation",
            Execute = () => throw new InvalidOperationException("native tool failed")
        };
        var skipped = new PendingOperation
        {
            Description = "Skipped operation",
            Execute = () => throw new InvalidOperationException("should not run")
        };

        queue.Enqueue(completed);
        queue.Enqueue(failed);
        queue.Enqueue(skipped);

        await queue.ApplyAllAsync(log, dialog, _ => { }, _ => { });

        Assert.DoesNotContain(completed, queue.Pending);
        Assert.Contains(failed, queue.Pending);
        Assert.Contains(skipped, queue.Pending);
        Assert.Equal(2, queue.Count);
        Assert.Contains("remain in the pending queue", dialog.LastError);
        Assert.Contains("preserved after failure", log.FullText);
    }

    [Fact]
    public async Task ApplyAllAsync_WhenAllOperationsSucceed_RemovesCompletedOperations()
    {
        var queue = new OperationQueue();
        var dialog = new CapturingDialogService();

        queue.Enqueue(new PendingOperation
        {
            Description = "First",
            Execute = () => Task.CompletedTask
        });
        queue.Enqueue(new PendingOperation
        {
            Description = "Second",
            Execute = () => Task.CompletedTask
        });

        await queue.ApplyAllAsync(new ActivityLog(), dialog, _ => { }, _ => { });

        Assert.Empty(queue.Pending);
        Assert.Equal("All 2 operation(s) applied successfully.", dialog.LastInfo);
    }

    private sealed class CapturingDialogService : IDialogService
    {
        public string LastError { get; private set; } = "";
        public string LastInfo { get; private set; } = "";

        public void ShowInfo(string message, string title) => LastInfo = message;
        public void ShowWarning(string message, string title) { }
        public void ShowError(string message, string title) => LastError = message;
        public bool Confirm(string message, string title) => true;
        public bool ConfirmWarning(string message, string title) => true;
        public bool ConfirmDanger(string message, string title) => true;
    }
}
