namespace PartitionPilot.Tests;

public class DiskHealthViewModelTests
{
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
}
