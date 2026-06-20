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
}
