namespace PartitionPilot.Tests;

public class WmiDiskServiceTests
{
    [Theory]
    [InlineData("", "''")]
    [InlineData("0", "'0'")]
    [InlineData(@"\\?\scsi#disk&ven_o'hara", @"'\\?\scsi#disk&ven_o''hara'")]
    [InlineData(@"C:\Images\Pilot's Disk.vhdx", @"'C:\Images\Pilot''s Disk.vhdx'")]
    public void WqlStringLiteral_EscapesApostrophesWithoutChangingBackslashes(string value, string expected)
    {
        Assert.Equal(expected, WmiDiskService.WqlStringLiteral(value));
    }

    [Fact]
    public void WqlStringLiteral_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => WmiDiskService.WqlStringLiteral(null!));
    }
}
