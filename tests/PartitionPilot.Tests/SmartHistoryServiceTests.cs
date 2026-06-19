namespace PartitionPilot.Tests;

public class SmartHistoryServiceTests
{
    [Fact]
    public void AnalyzeTrends_ReturnEmpty_WhenFewerThanTwoReadings()
    {
        var readings = new List<SmartReading> { new() { Wear = 10 } };
        var trends = SmartHistoryService.AnalyzeTrends(readings);
        Assert.Empty(trends);
    }

    [Fact]
    public void AnalyzeTrends_ReturnEmpty_WhenNoReadings()
    {
        var trends = SmartHistoryService.AnalyzeTrends(new List<SmartReading>());
        Assert.Empty(trends);
    }

    [Fact]
    public void AnalyzeTrends_DetectsReallocatedSectorIncrease()
    {
        var readings = new List<SmartReading>
        {
            new() { ReallocatedSectors = 0 },
            new() { ReallocatedSectors = 5 }
        };

        var trends = SmartHistoryService.AnalyzeTrends(readings);

        var trend = Assert.Single(trends);
        Assert.Equal("Reallocated Sectors", trend.Attribute);
        Assert.Equal("Increasing", trend.Direction);
        Assert.Equal("Warning", trend.Severity);
    }

    [Fact]
    public void AnalyzeTrends_IgnoresStableReallocatedSectors()
    {
        var readings = new List<SmartReading>
        {
            new() { ReallocatedSectors = 2 },
            new() { ReallocatedSectors = 2 }
        };

        var trends = SmartHistoryService.AnalyzeTrends(readings);
        Assert.DoesNotContain(trends, t => t.Attribute == "Reallocated Sectors");
    }

    [Fact]
    public void AnalyzeTrends_DetectsPendingSectorIncrease()
    {
        var readings = new List<SmartReading>
        {
            new() { PendingSectors = 0 },
            new() { PendingSectors = 3 }
        };

        var trends = SmartHistoryService.AnalyzeTrends(readings);
        Assert.Contains(trends, t => t.Attribute == "Pending Sectors" && t.Severity == "Warning");
    }

    [Fact]
    public void AnalyzeTrends_DetectsNvmeMediaErrorIncrease()
    {
        var readings = new List<SmartReading>
        {
            new() { NvmeMediaErrors = 0 },
            new() { NvmeMediaErrors = 1 }
        };

        var trends = SmartHistoryService.AnalyzeTrends(readings);
        Assert.Contains(trends, t => t.Attribute == "NVMe Media Errors");
    }

    [Theory]
    [InlineData(10, 20, "Info")]
    [InlineData(60, 72, "Warning")]
    [InlineData(80, 90, "Critical")]
    public void AnalyzeTrends_WearSeverityMatchesCurrentLevel(int startWear, int endWear, string expectedSeverity)
    {
        var readings = new List<SmartReading>
        {
            new() { Wear = startWear },
            new() { Wear = endWear }
        };

        var trends = SmartHistoryService.AnalyzeTrends(readings);
        var wearTrend = trends.FirstOrDefault(t => t.Attribute == "SSD Wear");

        Assert.NotNull(wearTrend);
        Assert.Equal(expectedSeverity, wearTrend.Severity);
        Assert.Equal("Increasing", wearTrend.Direction);
    }

    [Fact]
    public void AnalyzeTrends_IgnoresStableWear()
    {
        var readings = new List<SmartReading>
        {
            new() { Wear = 30 },
            new() { Wear = 30 }
        };

        var trends = SmartHistoryService.AnalyzeTrends(readings);
        Assert.DoesNotContain(trends, t => t.Attribute == "SSD Wear");
    }

    [Theory]
    [InlineData(100, 80, "Info")]
    [InlineData(40, 20, "Warning")]
    [InlineData(15, 8, "Critical")]
    public void AnalyzeTrends_NvmeSpareDecreaseSeverity(int startSpare, int endSpare, string expectedSeverity)
    {
        var readings = new List<SmartReading>
        {
            new() { NvmeAvailableSpare = startSpare },
            new() { NvmeAvailableSpare = endSpare }
        };

        var trends = SmartHistoryService.AnalyzeTrends(readings);
        var spareTrend = trends.FirstOrDefault(t => t.Attribute == "NVMe Available Spare");

        Assert.NotNull(spareTrend);
        Assert.Equal(expectedSeverity, spareTrend.Severity);
        Assert.Equal("Decreasing", spareTrend.Direction);
    }

    [Fact]
    public void AnalyzeTrends_IgnoresStableNvmeSpare()
    {
        var readings = new List<SmartReading>
        {
            new() { NvmeAvailableSpare = 90 },
            new() { NvmeAvailableSpare = 90 }
        };

        var trends = SmartHistoryService.AnalyzeTrends(readings);
        Assert.DoesNotContain(trends, t => t.Attribute == "NVMe Available Spare");
    }

    [Fact]
    public void AnalyzeTrends_DetectsHighAverageTemperature()
    {
        var readings = new List<SmartReading>
        {
            new() { Temperature = 60 },
            new() { Temperature = 62 },
            new() { Temperature = 58 }
        };

        var trends = SmartHistoryService.AnalyzeTrends(readings);
        var tempTrend = trends.FirstOrDefault(t => t.Attribute == "Temperature");

        Assert.NotNull(tempTrend);
        Assert.Equal("Warning", tempTrend.Severity);
    }

    [Fact]
    public void AnalyzeTrends_CriticalAtHighAverageTemperature()
    {
        var readings = new List<SmartReading>
        {
            new() { Temperature = 66 },
            new() { Temperature = 68 }
        };

        var trends = SmartHistoryService.AnalyzeTrends(readings);
        var tempTrend = trends.FirstOrDefault(t => t.Attribute == "Temperature");

        Assert.NotNull(tempTrend);
        Assert.Equal("Critical", tempTrend.Severity);
    }

    [Fact]
    public void AnalyzeTrends_IgnoresNormalTemperature()
    {
        var readings = new List<SmartReading>
        {
            new() { Temperature = 35 },
            new() { Temperature = 38 }
        };

        var trends = SmartHistoryService.AnalyzeTrends(readings);
        Assert.DoesNotContain(trends, t => t.Attribute == "Temperature");
    }

    [Fact]
    public void AnalyzeTrends_UsesOnlyLast10Readings()
    {
        var readings = new List<SmartReading>();
        for (int i = 0; i < 15; i++)
            readings.Add(new SmartReading { ReallocatedSectors = i < 5 ? 100 : 0 });

        var trends = SmartHistoryService.AnalyzeTrends(readings);
        Assert.DoesNotContain(trends, t => t.Attribute == "Reallocated Sectors");
    }

    [Fact]
    public void AnalyzeTrends_SkipsNullValues()
    {
        var readings = new List<SmartReading>
        {
            new() { ReallocatedSectors = null },
            new() { ReallocatedSectors = null }
        };

        var trends = SmartHistoryService.AnalyzeTrends(readings);
        Assert.DoesNotContain(trends, t => t.Attribute == "Reallocated Sectors");
    }

    [Fact]
    public void SmartReading_FromSmartData_CopiesAllFields()
    {
        var data = new SmartData
        {
            Temperature = 42,
            Wear = 15,
            ReallocatedSectors = 3,
            PendingSectors = 1,
            PowerOnHours = 12000,
            TotalBytesWritten = 1_000_000_000_000,
            NvmeAvailableSpare = 85,
            NvmeMediaErrors = 0
        };

        var reading = SmartReading.FromSmartData(data);

        Assert.Equal(42, reading.Temperature);
        Assert.Equal(15, reading.Wear);
        Assert.Equal(3, reading.ReallocatedSectors);
        Assert.Equal(1, reading.PendingSectors);
        Assert.Equal(12000, reading.PowerOnHours);
        Assert.Equal(1_000_000_000_000, reading.TotalBytesWritten);
        Assert.Equal(85, reading.NvmeAvailableSpare);
        Assert.Equal(0, reading.NvmeMediaErrors);
        Assert.True(reading.Timestamp > DateTimeOffset.UtcNow.AddSeconds(-5));
    }
}
