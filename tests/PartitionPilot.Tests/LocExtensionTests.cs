using System.Globalization;
using System.Resources;

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

    [Fact]
    public void PseudoLocale_HasAllKeysFromDefaultResources()
    {
        var asm = typeof(LocExtension).Assembly;
        var rm = new ResourceManager("PartitionPilot.Properties.Strings", asm);

        var defaultSet = rm.GetResourceSet(CultureInfo.InvariantCulture, true, false);
        Assert.NotNull(defaultSet);

        var pseudoSet = rm.GetResourceSet(new CultureInfo("qps-ploc"), true, false);

        var defaultKeys = new List<string>();
        var enumerator = defaultSet!.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (enumerator.Key is string key)
                defaultKeys.Add(key);
        }

        Assert.True(defaultKeys.Count >= 130, $"Expected >=130 resource keys, found {defaultKeys.Count}");

        if (pseudoSet is not null)
        {
            var pseudoKeys = new HashSet<string>();
            var pseudoEnum = pseudoSet.GetEnumerator();
            while (pseudoEnum.MoveNext())
            {
                if (pseudoEnum.Key is string key)
                    pseudoKeys.Add(key);
            }

            var missing = defaultKeys.Where(k => !pseudoKeys.Contains(k)).ToList();
            Assert.True(missing.Count == 0,
                $"Pseudo-locale is missing {missing.Count} key(s): {string.Join(", ", missing.Take(10))}");
        }
    }
}
