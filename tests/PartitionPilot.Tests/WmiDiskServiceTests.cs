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

    [Theory]
    [InlineData("0", 0)]
    [InlineData("12", 12)]
    public void ParseDeviceNumber_AcceptsNonNegativeNumbers(string deviceId, int expected)
    {
        Assert.Equal(expected, WmiDiskService.ParseDeviceNumber(deviceId));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1; Remove-Item C:\\")]
    [InlineData("")]
    public void ParseDeviceNumber_RejectsInvalidDeviceIds(string deviceId)
    {
        Assert.Throws<ArgumentException>(() => WmiDiskService.ParseDeviceNumber(deviceId));
    }
}
