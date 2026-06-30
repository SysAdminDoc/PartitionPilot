namespace PartitionPilot.Tests;

public class VssSnapshotServiceTests
{
    [Fact]
    public void ParseWriterHealth_ReportsHealthyWriters()
    {
        var report = VssSnapshotService.ParseWriterHealth(HealthyWriterOutput);

        Assert.True(report.IsHealthy);
        Assert.Equal(2, report.Writers.Count);
        Assert.All(report.Writers, writer => Assert.True(writer.IsHealthy));
        Assert.Contains("2 writer(s) stable", report.Summary);
    }

    [Fact]
    public void ParseWriterHealth_ReportsFailedWriters()
    {
        var report = VssSnapshotService.ParseWriterHealth(FailedWriterOutput);

        Assert.False(report.IsHealthy);
        var writer = Assert.Single(report.UnhealthyWriters);
        Assert.Equal("SqlServerWriter", writer.Name);
        Assert.Contains("Retryable error", writer.LastError);
        Assert.Contains("SqlServerWriter", report.Summary);
    }

    [Fact]
    public void ParseWriterHealth_TreatsMissingWritersAsUnhealthy()
    {
        var report = VssSnapshotService.ParseWriterHealth("No writers found.");

        Assert.False(report.IsHealthy);
        Assert.Empty(report.Writers);
        Assert.Contains("No VSS writers", report.Summary);
    }

    [Fact]
    public async Task EnsureWritersHealthyAsync_ThrowsAndLogsWhenWriterFailed()
    {
        var log = new TestLog();
        var runner = new FakeRunner(FailedWriterOutput);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            VssSnapshotService.EnsureWritersHealthyAsync(runner, log, TestContext.Current.CancellationToken));

        Assert.Contains("VSS writer health preflight failed", ex.Message);
        Assert.Contains("SqlServerWriter", ex.Message);
        Assert.Contains(log.Messages, message =>
            message.Contains("VSS writer health failed", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("SqlServerWriter", StringComparison.OrdinalIgnoreCase));
    }

    private const string HealthyWriterOutput = """
        Writer name: 'System Writer'
           Writer Id: {e8132975-6f93-4464-a53e-1050253ae220}
           Writer Instance Id: {8fc33a7f-1234-46f8-9c18-111111111111}
           State: [1] Stable
           Last error: No error

        Writer name: 'WMI Writer'
           Writer Id: {a6ad56c2-b509-4e6c-bb19-49d8f43532f0}
           Writer Instance Id: {5a1f9f7a-1234-4d2e-a8d8-222222222222}
           State: [1] Stable
           Last error: No error
        """;

    private const string FailedWriterOutput = """
        Writer name: 'System Writer'
           Writer Id: {e8132975-6f93-4464-a53e-1050253ae220}
           Writer Instance Id: {8fc33a7f-1234-46f8-9c18-111111111111}
           State: [1] Stable
           Last error: No error

        Writer name: 'SqlServerWriter'
           Writer Id: {a65faa63-5ea8-4ebc-9dbd-a0c4db26912a}
           Writer Instance Id: {d86a2c55-1234-493f-a5a0-333333333333}
           State: [9] Failed
           Last error: Retryable error
        """;

    private sealed class TestLog : IActivityLog
    {
        public List<string> Messages { get; } = new();

        public void Log(string message) => Messages.Add(message);
    }

    private sealed class FakeRunner(string writerOutput) : IProcessRunner
    {
        public Task<string> RunDiskpartAsync(string script, IActivityLog? log = null, CancellationToken ct = default) =>
            Task.FromResult("");

        public Task<string> RunPowerShellAsync(string command, IActivityLog? log = null, CancellationToken ct = default) =>
            Task.FromResult("");

        public Task<string> RunExeAsync(string fileName, string arguments, IActivityLog? log = null,
            bool ignoreStderrOnSuccess = false, CancellationToken ct = default)
        {
            Assert.Equal("vssadmin", fileName);
            Assert.Equal("list writers", arguments);
            return Task.FromResult(writerOutput);
        }
    }
}
