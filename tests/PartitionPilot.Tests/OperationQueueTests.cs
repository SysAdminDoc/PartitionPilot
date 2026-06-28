using System.Text;

namespace PartitionPilot.Tests;

public class OperationQueueTests
{
    private sealed class TestLog : IActivityLog
    {
        private readonly StringBuilder _sb = new();
        public string FullText => _sb.ToString();
        public void Log(string message) => _sb.AppendLine(message);
    }

    [Fact]
    public async Task ApplyAllAsync_WhenOperationFails_PreservesFailedAndSkippedOperations()
    {
        var queue = new OperationQueue();
        var log = new TestLog();
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

        await queue.ApplyAllAsync(new TestLog(), dialog, _ => { }, _ => { });

        Assert.Empty(queue.Pending);
        Assert.Equal("All 2 operation(s) applied successfully.", dialog.LastInfo);
    }

    [Fact]
    public async Task ApplyAllAsync_RunsTargetValidationBeforeExecute()
    {
        var queue = new OperationQueue();
        var log = new TestLog();
        var dialog = new CapturingDialogService();
        var executed = false;

        queue.Enqueue(new PendingOperation
        {
            Description = "Delete partition",
            RiskLevel = "Destructive",
            ValidateTarget = () => throw new InvalidOperationException("Target disk identity changed"),
            Execute = () =>
            {
                executed = true;
                return Task.CompletedTask;
            }
        });

        await queue.ApplyAllAsync(log, dialog, _ => { }, _ => { });

        Assert.False(executed);
        Assert.Single(queue.Pending);
        Assert.Contains("Target disk identity changed", dialog.LastError);
        Assert.Contains("Target disk identity changed", log.FullText);
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
