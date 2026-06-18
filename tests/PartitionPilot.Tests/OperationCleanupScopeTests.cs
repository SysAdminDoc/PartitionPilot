namespace PartitionPilot.Tests;

public class OperationCleanupScopeTests
{
    [Fact]
    public async Task DisposeAsync_RunsActiveCleanupsInReverseOrder()
    {
        var log = new ActivityLog();
        var calls = new List<string>();
        var scope = new OperationCleanupScope(log);

        scope.Register("first", () =>
        {
            calls.Add("first");
            return Task.CompletedTask;
        }, "first recovery");
        scope.Register("second", () =>
        {
            calls.Add("second");
            return Task.CompletedTask;
        }, "second recovery");

        await scope.DisposeAsync();

        Assert.Equal(new[] { "second", "first" }, calls);
    }

    [Fact]
    public async Task DisposeAsync_SkipsCompletedRegistrations()
    {
        var log = new ActivityLog();
        var calls = new List<string>();
        var scope = new OperationCleanupScope(log);

        var registration = scope.Register("skip", () =>
        {
            calls.Add("skip");
            return Task.CompletedTask;
        }, "skip recovery");
        registration.Complete();

        await scope.DisposeAsync();

        Assert.Empty(calls);
    }

    [Fact]
    public async Task DisposeAsync_LogsFailuresWithRecoveryGuidance()
    {
        var log = new ActivityLog();
        var scope = new OperationCleanupScope(log);

        scope.Register("bad cleanup", () => throw new InvalidOperationException("not available"), "run manual cleanup");

        await scope.DisposeAsync();

        Assert.Contains("Cleanup failed: bad cleanup", log.FullText);
        Assert.Contains("run manual cleanup", log.FullText);
    }

    [Fact]
    public async Task RegisterFileDelete_RemovesTemporaryFile()
    {
        var log = new ActivityLog();
        var tempFile = Path.GetTempFileName();
        var scope = new OperationCleanupScope(log);

        scope.RegisterFileDelete(tempFile);
        await scope.DisposeAsync();

        Assert.False(File.Exists(tempFile));
    }
}
