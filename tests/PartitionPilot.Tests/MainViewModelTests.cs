namespace PartitionPilot.Tests;

public class MainViewModelTests
{
    [Fact]
    public void VersionText_ComesFromAssemblyMetadata()
    {
        Assert.Equal($"PartitionPilot v{UpdateService.GetCurrentVersion()}", MainViewModel.GetVersionText());
    }
}
