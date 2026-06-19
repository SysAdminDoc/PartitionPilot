namespace PartitionPilot.Tests;

public class PartitionsViewModelTests
{
    [Theory]
    [InlineData("Recovery", true)]
    [InlineData("recovery", true)]
    [InlineData("Basic", false)]
    [InlineData("System", false)]
    public void IsRecoveryPartition_UsesPartitionType(string type, bool expected)
    {
        var partition = new PartitionInfo { Type = type };

        Assert.Equal(expected, PartitionsViewModel.IsRecoveryPartition(partition));
    }

    [Fact]
    public void IsForwardAdjacentMergePair_AllowsNextPartitionOnSameDisk()
    {
        var primary = Partition(1, 'D', 0, 100);
        var secondary = Partition(2, 'E', 100, 50);
        var partitions = new[] { primary, secondary };

        Assert.True(PartitionsViewModel.IsForwardAdjacentMergePair(partitions, primary, secondary));
    }

    [Fact]
    public void IsForwardAdjacentMergePair_RejectsSkippedPartition()
    {
        var primary = Partition(1, 'D', 0, 100);
        var middle = Partition(2, 'E', 100, 50);
        var secondary = Partition(3, 'F', 150, 50);
        var partitions = new[] { primary, middle, secondary };

        Assert.False(PartitionsViewModel.IsForwardAdjacentMergePair(partitions, primary, secondary));
    }

    [Fact]
    public void IsForwardAdjacentMergePair_RejectsReverseMerge()
    {
        var primary = Partition(2, 'E', 100, 50);
        var secondary = Partition(1, 'D', 0, 100);
        var partitions = new[] { secondary, primary };

        Assert.False(PartitionsViewModel.IsForwardAdjacentMergePair(partitions, primary, secondary));
    }

    [Fact]
    public void IsForwardAdjacentMergePair_RejectsMissingDriveLetter()
    {
        var primary = Partition(1, 'D', 0, 100);
        var secondary = Partition(2, null, 100, 50);
        var partitions = new[] { primary, secondary };

        Assert.False(PartitionsViewModel.IsForwardAdjacentMergePair(partitions, primary, secondary));
    }

    private static PartitionInfo Partition(int number, char? letter, long offset, long size) => new()
    {
        DiskNumber = 0,
        PartitionNumber = number,
        DriveLetter = letter,
        Offset = offset,
        Size = size,
        Type = "Basic",
    };
}
