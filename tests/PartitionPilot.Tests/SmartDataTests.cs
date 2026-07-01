namespace PartitionPilot.Tests;

public class SmartDataTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(50)]
    public void Health_TreatsLowWearIndicatorAsGood(int wear)
    {
        var smart = new SmartData { Wear = wear };

        Assert.Equal(HealthStatus.Good, smart.Health);
        Assert.Contains("normal range", smart.HealthReason);
    }

    [Theory]
    [InlineData(85)]
    [InlineData(94)]
    public void Health_WarnsWhenWearIndicatorApproachesLimit(int wear)
    {
        var smart = new SmartData { Wear = wear };

        Assert.Equal(HealthStatus.Warning, smart.Health);
        Assert.Contains($"{wear}%", smart.HealthReason);
    }

    [Theory]
    [InlineData(95)]
    [InlineData(100)]
    public void Health_IsCriticalWhenWearIndicatorNearsEstimatedLimit(int wear)
    {
        var smart = new SmartData { Wear = wear };

        Assert.Equal(HealthStatus.Critical, smart.Health);
        Assert.Contains("estimated wear limit", smart.HealthReason);
    }

    [Fact]
    public void CriticalWarningFlags_ParsesBitfield()
    {
        var smart = new SmartData { NvmeCriticalWarning = 0x05 };
        var flags = smart.CriticalWarningFlags;
        Assert.Contains("Available spare low", flags);
        Assert.Contains("Reliability degraded", flags);
        Assert.Equal(2, flags.Count);
    }

    [Fact]
    public void CriticalWarningFlags_EmptyWhenZero()
    {
        var smart = new SmartData { NvmeCriticalWarning = 0 };
        Assert.Empty(smart.CriticalWarningFlags);
    }

    [Fact]
    public void CriticalWarningFlags_EmptyWhenNull()
    {
        var smart = new SmartData();
        Assert.Empty(smart.CriticalWarningFlags);
    }

    [Fact]
    public void CriticalWarningFlags_AllFiveFlags()
    {
        var smart = new SmartData { NvmeCriticalWarning = 0x1F };
        Assert.Equal(5, smart.CriticalWarningFlags.Count);
    }

    [Fact]
    public void AttributeMetadata_MapsKnownAtaAttributes()
    {
        var attribute = new SmartAttribute { Id = 5, Name = "Vendor reallocated", RawValue = 2 };
        var smart = new SmartData { AllAttributes = new List<SmartAttribute> { attribute } };

        Assert.Equal("Reallocated Sector Count", attribute.DisplayName);
        Assert.Equal("Warning", attribute.AdvisorySeverity);
        Assert.True(attribute.HasCuratedMetadata);
        Assert.Equal(SmartAttributeMetadataService.MetadataVersion, attribute.MetadataVersion);
        Assert.Contains(smart.Advisories, a => a.Name == "Reallocated Sector Count" && a.Severity == "Warning");
    }

    [Fact]
    public void AttributeMetadata_PreservesUnknownVendorAttributes()
    {
        var attribute = new SmartAttribute { Id = 250, Name = "Vendor Private Counter", RawValue = 123 };
        var smart = new SmartData { AllAttributes = new List<SmartAttribute> { attribute } };

        Assert.Equal("Vendor Private Counter", attribute.DisplayName);
        Assert.Equal("Unknown", attribute.AdvisorySeverity);
        Assert.False(attribute.HasCuratedMetadata);
        Assert.Contains("No curated metadata", attribute.AdvisoryText);
        Assert.Empty(smart.Advisories);
    }

    [Fact]
    public void Advisories_IncludeNvmeTopLevelHealthWarnings()
    {
        var smart = new SmartData
        {
            NvmeAvailableSpare = 4,
            NvmeMediaErrors = 3,
            NvmeCriticalWarning = 0x04
        };

        Assert.Contains(smart.Advisories, a => a.Name == "NVMe Available Spare" && a.Severity == "Critical");
        Assert.Contains(smart.Advisories, a => a.Name == "NVMe Media Errors" && a.Severity == "Warning");
        Assert.Contains(smart.Advisories, a => a.Name == "Reliability degraded" && a.Severity == "Critical");
        Assert.All(smart.Advisories, a => Assert.Equal(SmartAttributeMetadataService.MetadataVersion, a.MetadataVersion));
    }
}
