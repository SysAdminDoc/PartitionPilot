namespace PartitionPilot.Tests;

public class LocExtensionTests
{
    [Theory]
    [InlineData("AppTitle", "PartitionPilot")]
    [InlineData("TabPartitions", "Partitions")]
    [InlineData("TabSnapshots", "Snapshots")]
    [InlineData("TabDiskHealth", "Disk Health")]
    [InlineData("TabTools", "Tools")]
    [InlineData("TabDiskImages", "Disk Images")]
    [InlineData("TabDiskUsage", "Disk Usage")]
    [InlineData("TabDiskCloning", "Disk Cloning")]
    [InlineData("Ready", "Ready")]
    [InlineData("ActivityLog", "Activity Log")]
    public void Get_ReturnsExpectedString(string key, string expected)
    {
        Assert.Equal(expected, LocExtension.Get(key));
    }

    [Fact]
    public void Get_ReturnsBracketedKey_ForMissingKey()
    {
        var result = LocExtension.Get("NonExistentKeyXyz");
        Assert.Equal("[NonExistentKeyXyz]", result);
    }

    [Theory]
    [InlineData("Refresh")]
    [InlineData("ExportLog")]
    [InlineData("SupportBundle")]
    [InlineData("Elevate")]
    [InlineData("Good")]
    [InlineData("Warning")]
    [InlineData("Error")]
    [InlineData("Cancel")]
    [InlineData("Apply")]
    [InlineData("OK")]
    public void Get_AllCommonKeys_ReturnNonEmpty(string key)
    {
        var result = LocExtension.Get(key);
        Assert.False(string.IsNullOrEmpty(result));
        Assert.DoesNotContain("[", result);
    }

    [Theory]
    [InlineData("TabPartitionsSubtitle")]
    [InlineData("TabSnapshotsSubtitle")]
    [InlineData("TabDiskHealthSubtitle")]
    [InlineData("TabToolsSubtitle")]
    [InlineData("TabDiskImagesSubtitle")]
    [InlineData("TabDiskUsageSubtitle")]
    [InlineData("TabDiskCloningSubtitle")]
    public void Get_AllSubtitleKeys_ReturnNonEmpty(string key)
    {
        var result = LocExtension.Get(key);
        Assert.False(string.IsNullOrEmpty(result));
        Assert.DoesNotContain("[", result);
    }

    [Theory]
    [InlineData("Temperature")]
    [InlineData("WearUsed")]
    [InlineData("ReallocatedSectors")]
    [InlineData("PendingSectors")]
    [InlineData("NvmeAvailableSpare")]
    [InlineData("NvmeMediaErrors")]
    [InlineData("NotAvailable")]
    public void Get_SmartLabels_ReturnNonEmpty(string key)
    {
        var result = LocExtension.Get(key);
        Assert.False(string.IsNullOrEmpty(result));
    }
}
