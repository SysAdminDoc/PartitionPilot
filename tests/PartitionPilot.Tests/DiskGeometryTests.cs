namespace PartitionPilot.Tests;

public class DiskGeometryTests
{
    [Theory]
    [InlineData(0, 512, 0)]
    [InlineData(7, 512, 3584)]
    [InlineData(7, 4096, 28672)]
    public void GetByteOffset_UsesLogicalSectorSize(long lba, int logicalSectorSize, long expected)
    {
        Assert.Equal(expected, DiskGeometry.GetByteOffset(lba, logicalSectorSize));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(511)]
    [InlineData(1000)]
    [InlineData(131072)]
    public void NormalizeLogicalSectorSize_FallsBackForInvalidValues(int logicalSectorSize)
    {
        Assert.Equal(DiskGeometry.DefaultLogicalSectorSize, DiskGeometry.NormalizeLogicalSectorSize(logicalSectorSize));
    }

    [Fact]
    public void GetByteOffset_RejectsNegativeLba()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DiskGeometry.GetByteOffset(-1, 4096));
    }
}
