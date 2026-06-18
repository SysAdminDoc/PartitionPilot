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
}
