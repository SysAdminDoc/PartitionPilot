namespace PartitionPilot.Tests;

public class SectorCloneServiceTests
{
    [Fact]
    public void ValidateClone_ThrowsForSameDisk()
    {
        var disk = new DiskInfo { Number = 0, Size = 1000 };
        Assert.Throws<InvalidOperationException>(() =>
            SectorCloneService.ValidateClone(disk, disk));
    }

    [Fact]
    public void ValidateClone_ThrowsWhenDestinationSmaller()
    {
        var source = new DiskInfo { Number = 0, Size = 500_000_000_000 };
        var dest = new DiskInfo { Number = 1, Size = 256_000_000_000 };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SectorCloneService.ValidateClone(source, dest));
        Assert.Contains("smaller", ex.Message);
    }

    [Fact]
    public void ValidateClone_ThrowsWhenDestinationPooled()
    {
        var source = new DiskInfo { Number = 0, Size = 500_000_000_000 };
        var dest = new DiskInfo { Number = 1, Size = 500_000_000_000, StoragePoolName = "MyPool" };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SectorCloneService.ValidateClone(source, dest));
        Assert.Contains("pooled", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateClone_ThrowsWhenSourcePooled()
    {
        var source = new DiskInfo { Number = 0, Size = 500_000_000_000, StoragePoolName = "Pool1" };
        var dest = new DiskInfo { Number = 1, Size = 500_000_000_000 };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SectorCloneService.ValidateClone(source, dest));
        Assert.Contains("pooled", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateClone_AllowsEqualSizeDisks()
    {
        var source = new DiskInfo { Number = 0, Size = 500_000_000_000 };
        var dest = new DiskInfo { Number = 1, Size = 500_000_000_000 };

        SectorCloneService.ValidateClone(source, dest);
    }

    [Fact]
    public void ValidateClone_AllowsLargerDestination()
    {
        var source = new DiskInfo { Number = 0, Size = 256_000_000_000 };
        var dest = new DiskInfo { Number = 1, Size = 500_000_000_000 };

        SectorCloneService.ValidateClone(source, dest);
    }

    [Fact]
    public void SectorCloneProgress_PercentComplete_CalculatesCorrectly()
    {
        var progress = new SectorCloneProgress { BytesCopied = 250, TotalBytes = 1000 };
        Assert.Equal(25.0, progress.PercentComplete);
    }

    [Fact]
    public void SectorCloneProgress_PercentComplete_HandlesZeroTotal()
    {
        var progress = new SectorCloneProgress { BytesCopied = 100, TotalBytes = 0 };
        Assert.Equal(0, progress.PercentComplete);
    }

    [Fact]
    public void SectorCloneProgress_RateText_FormatsWhenPositive()
    {
        var progress = new SectorCloneProgress { BytesPerSecond = 104_857_600 };
        Assert.Contains("/s", progress.RateText);
    }

    [Fact]
    public void SectorCloneProgress_RateText_EmptyWhenZero()
    {
        var progress = new SectorCloneProgress { BytesPerSecond = 0 };
        Assert.Equal("", progress.RateText);
    }

    [Fact]
    public void SectorCloneProgress_ProgressText_IncludesPercent()
    {
        var progress = new SectorCloneProgress { BytesCopied = 500, TotalBytes = 1000 };
        Assert.Contains("50.0%", progress.ProgressText);
    }
}
