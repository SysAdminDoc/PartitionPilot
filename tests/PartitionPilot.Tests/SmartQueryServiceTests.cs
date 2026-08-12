namespace PartitionPilot.Tests;

public class SmartQueryServiceTests
{
    [Theory]
    [InlineData("/hdd/0", 0)]
    [InlineData("/ssd/7", 7)]
    [InlineData("/nvme/12", 12)]
    public void GetStorageDiskNumber_ParsesLibreHardwareMonitorIdentifier(string identifier, int expected)
    {
        Assert.Equal(expected, SmartQueryService.GetStorageDiskNumber(identifier));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/nvme/")]
    [InlineData("/nvme/not-a-number")]
    [InlineData("/nvme/-1")]
    public void GetStorageDiskNumber_RejectsInvalidIdentifier(string? identifier)
    {
        Assert.Null(SmartQueryService.GetStorageDiskNumber(identifier));
    }
}
