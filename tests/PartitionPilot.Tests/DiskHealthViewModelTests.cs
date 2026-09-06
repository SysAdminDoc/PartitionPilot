namespace PartitionPilot.Tests;

public class DiskHealthViewModelTests
{
    [Fact]
    public void SimulationMode_ReportsSelfTestsWithoutLaunchingSmartctl()
    {
        var runner = new RejectingProcessRunner();
        var viewModel = new DiskHealthViewModel(
            new SimulatedDiskService(),
            runner,
            new ActivityLog());

        viewModel.SelectedDisk = new PhysicalDiskInfo
        {
            DeviceId = "0",
            FriendlyName = "Simulated NVMe",
            BusType = "NVMe"
        };

        Assert.Equal("Simulated", viewModel.SmartSelfTestCapability.Status);
        Assert.True(viewModel.SmartSelfTestCapability.CanRunSelfTest);
        Assert.Equal(0, runner.CallCount);
        viewModel.Dispose();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var viewModel = new DiskHealthViewModel(
            new SimulatedDiskService(),
            new ProcessRunner(),
            new ActivityLog());

        viewModel.Dispose();
        viewModel.Dispose();

        Assert.False(viewModel.IsMonitoring);
        Assert.False(viewModel.IsPerfMonitoring);
    }

    private sealed class RejectingProcessRunner : IProcessRunner
    {
        public int CallCount { get; private set; }

        public Task<string> RunDiskpartAsync(string script, IActivityLog? log = null, CancellationToken ct = default) =>
            Reject();

        public Task<string> RunPowerShellAsync(string command, IActivityLog? log = null, CancellationToken ct = default) =>
            Reject();

        public Task<string> RunExeAsync(
            string fileName,
            string arguments,
            IActivityLog? log = null,
            bool ignoreStderrOnSuccess = false,
            CancellationToken ct = default) => Reject();

        private Task<string> Reject()
        {
            CallCount++;
            throw new InvalidOperationException("Simulation mode must not start external diagnostics.");
        }
    }
}
