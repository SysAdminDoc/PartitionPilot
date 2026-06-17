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
}
