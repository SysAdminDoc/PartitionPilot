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

    [Fact]
    public async Task CreateSnapshotAsync_UsesTheShadowDeviceObjectAsTheCapturePath()
    {
        var log = new TestLog();
        var provider = new FakeShadowCopyProvider(
            "{18BDD207-FB1B-4860-B918-B94C9EBF1F1F}",
            @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7");

        var snapshot = await VssSnapshotService.CreateSnapshotAsync(
            'c', provider, log, TestContext.Current.CancellationToken);

        Assert.Equal('c', provider.CreatedVolume);
        Assert.Equal("{18BDD207-FB1B-4860-B918-B94C9EBF1F1F}", snapshot.ShadowCopyId);
        Assert.Equal(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7\", snapshot.ShadowCopyPath);
        Assert.Contains(log.Messages, m => m.Contains("VSS shadow copy created", StringComparison.OrdinalIgnoreCase));

        await snapshot.DisposeAsync();
        Assert.Equal("{18BDD207-FB1B-4860-B918-B94C9EBF1F1F}", provider.DeletedShadowCopyId);
    }

    [Fact]
    public async Task CreateSnapshotAsync_RejectsAnEmptyDevicePath()
    {
        var provider = new FakeShadowCopyProvider("{GUID}", "   ");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            VssSnapshotService.CreateSnapshotAsync('c', provider, new TestLog(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProbeCreationAsync_PassesAndCleansUpWhenCreationSucceeds()
    {
        var provider = new FakeShadowCopyProvider(
            "{18BDD207-FB1B-4860-B918-B94C9EBF1F1F}",
            @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7");

        var probe = await VssSnapshotService.ProbeCreationAsync(
            'c', provider, new TestLog(), TestContext.Current.CancellationToken);

        Assert.True(probe.CanCreate);
        Assert.Equal("{18BDD207-FB1B-4860-B918-B94C9EBF1F1F}", provider.DeletedShadowCopyId);
    }

    [Fact]
    public async Task ProbeCreationAsync_FailsWhenTheShadowCopyCannotBeDeleted()
    {
        // Snapshot disposal swallows cleanup failures by design. The probe must not inherit that:
        // reporting success while leaving an orphaned shadow copy behind lets them accumulate silently.
        var provider = new UndeletableShadowCopyProvider(
            "{18BDD207-FB1B-4860-B918-B94C9EBF1F1F}",
            @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7");

        var probe = await VssSnapshotService.ProbeCreationAsync(
            'c', provider, new TestLog(), TestContext.Current.CancellationToken);

        Assert.False(probe.CanCreate);
        Assert.Contains("could not delete it", probe.Detail);
        Assert.Contains("{18BDD207-FB1B-4860-B918-B94C9EBF1F1F}", probe.Remediation);
    }

    [Fact]
    public async Task ProbeCreationAsync_FailsWithRemediationWhenCreationThrows()
    {
        var provider = new ThrowingShadowCopyProvider(
            "Win32_ShadowCopy.Create returned 1: Access denied — shadow copy creation requires an elevated session.");

        var probe = await VssSnapshotService.ProbeCreationAsync(
            'c', provider, new TestLog(), TestContext.Current.CancellationToken);

        Assert.False(probe.CanCreate);
        Assert.Contains("Access denied", probe.Detail);
        Assert.NotEmpty(probe.Remediation);
    }

    [Theory]
    [InlineData(1u, "Access denied")]
    [InlineData(4u, "local NTFS volume")]
    [InlineData(6u, "shadowstorage")]
    [InlineData(99u, "Undocumented return code 99")]
    public void DescribeCreateReturnCode_ExplainsDocumentedFailures(uint code, string expectedFragment)
    {
        Assert.Contains(expectedFragment, WmiShadowCopyProvider.DescribeCreateReturnCode(code));
    }

    private sealed class FakeShadowCopyProvider(string shadowId, string deviceObject) : IShadowCopyProvider
    {
        public char CreatedVolume { get; private set; }
        public string? DeletedShadowCopyId { get; private set; }

        public Task<ShadowCopyCreateResult> CreateAsync(char volumeLetter, CancellationToken ct = default)
        {
            CreatedVolume = volumeLetter;
            return Task.FromResult(new ShadowCopyCreateResult(shadowId, deviceObject));
        }

        public Task DeleteAsync(string shadowCopyId, CancellationToken ct = default)
        {
            DeletedShadowCopyId = shadowCopyId;
            return Task.CompletedTask;
        }
    }

    private sealed class UndeletableShadowCopyProvider(string shadowId, string deviceObject) : IShadowCopyProvider
    {
        public Task<ShadowCopyCreateResult> CreateAsync(char volumeLetter, CancellationToken ct = default) =>
            Task.FromResult(new ShadowCopyCreateResult(shadowId, deviceObject));

        public Task DeleteAsync(string shadowCopyId, CancellationToken ct = default) =>
            throw new InvalidOperationException("Access denied deleting the shadow copy.");
    }

    private sealed class ThrowingShadowCopyProvider(string message) : IShadowCopyProvider
    {
        public Task<ShadowCopyCreateResult> CreateAsync(char volumeLetter, CancellationToken ct = default) =>
            throw new InvalidOperationException(message);

        public Task DeleteAsync(string shadowCopyId, CancellationToken ct = default) => Task.CompletedTask;
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
