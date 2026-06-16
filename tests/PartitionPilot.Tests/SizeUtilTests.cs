namespace PartitionPilot.Tests;

public class SizeUtilTests
{
    [Theory]
    [InlineData(0, "0 KB")]
    [InlineData(512, "0 KB")]
    [InlineData(1024, "1 KB")]
    [InlineData(1_048_576, "1 MB")]
    [InlineData(1_073_741_824, "1 GB")]
    [InlineData(1_099_511_627_776, "1 TB")]
    [InlineData(500_000_000_000, "465.66 GB")]
    [InlineData(256_000_000_000, "238.42 GB")]
    public void Format_ReturnsHumanReadableSize(long bytes, string expected)
    {
        Assert.Equal(expected, SizeUtil.Format(bytes));
    }
}
