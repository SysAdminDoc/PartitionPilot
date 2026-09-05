namespace PartitionPilot.Tests;

public class AtaHealthServiceTests
{
    private const byte SupportedAndValid = 0xC0;
    private const byte SupportedValidNormalized = 0xE0;

    [Fact]
    public void TryReadStatistic_ReadsA56BitValueWhenSupportedAndValid()
    {
        var page = Page(1, (8, 5319, SupportedAndValid));

        Assert.True(AtaHealthService.TryReadStatistic(page, 8, out var value));
        Assert.Equal(5319, value);
    }

    [Theory]
    [InlineData((byte)0x00)] // neither supported nor valid
    [InlineData((byte)0x80)] // supported but not valid
    [InlineData((byte)0x40)] // valid bit without supported
    public void TryReadStatistic_IgnoresAnEntryThatIsNotBothSupportedAndValid(byte flags)
    {
        // An unsupported statistic reads as zero on the wire. Reporting that as a real zero would claim
        // a drive has never been powered on.
        var page = Page(1, (8, 5319, flags));

        Assert.False(AtaHealthService.TryReadStatistic(page, 8, out var value));
        Assert.Equal(0, value);
    }

    [Fact]
    public void TryReadStatistic_ReadsTheFullFiftySixBitRangeWithoutSignExtension()
    {
        var page = Page(1, (8, 0x00FFFFFFFFFFFFFF, SupportedAndValid));

        Assert.True(AtaHealthService.TryReadStatistic(page, 8, out var value));
        Assert.Equal(0x00FFFFFFFFFFFFFF, value);
    }

    [Theory]
    [InlineData(null, 8)]
    [InlineData(new byte[] { 1, 2, 3 }, 0)]
    public void TryReadStatistic_RejectsAPageThatCannotHoldTheEntry(byte[]? page, int offset)
    {
        Assert.False(AtaHealthService.TryReadStatistic(page, offset, out _));
    }

    [Fact]
    public void TryReadStatistic_RejectsAnEntryRunningPastTheEndOfThePage()
    {
        var page = Page(1, (8, 1, SupportedAndValid));

        Assert.False(AtaHealthService.TryReadStatistic(page, page.Length - 4, out _));
    }

    [Fact]
    public void ApplyStatistics_MapsTheRealValuesReadFromASamsung870Evo()
    {
        // Values captured from a Samsung SSD 870 EVO 4TB on 2026-09-04 through the same IOCTL.
        var general = Page(1,
            (8, 228, SupportedAndValid),            // power-on resets
            (16, 5319, SupportedAndValid),          // power-on hours
            (24, 305220134794, SupportedAndValid),  // logical sectors written
            (40, 367299987007, SupportedAndValid)); // logical sectors read
        var temperature = Page(5, (8, 45, SupportedAndValid), (32, 54, SupportedAndValid));
        var solidState = Page(7, (8, 2, SupportedValidNormalized));

        var data = new SmartData();
        Assert.True(AtaHealthService.ApplyStatistics(data, general, temperature, solidState, 512));

        Assert.Equal(5319, data.PowerOnHours);
        Assert.Equal(228, data.PowerCycleCount);
        Assert.Equal(305220134794L * 512, data.TotalBytesWritten);
        Assert.Equal(367299987007L * 512, data.TotalBytesRead);
        Assert.Equal(45, data.Temperature);
        Assert.Equal(54, data.AtaHighestTemperature);
        Assert.Equal(2, data.Wear);
    }

    [Fact]
    public void ApplyStatistics_ScalesSectorCountsBy4KnSectorSize()
    {
        var general = Page(1, (24, 1000, SupportedAndValid));

        var data = new SmartData();
        AtaHealthService.ApplyStatistics(data, general, null, null, 4096);

        Assert.Equal(1000L * 4096, data.TotalBytesWritten);
    }

    [Fact]
    public void ApplyStatistics_LeavesValuesAlreadySuppliedByAnotherSourceAlone()
    {
        var general = Page(1, (16, 5319, SupportedAndValid));
        var data = new SmartData { PowerOnHours = 42 };

        AtaHealthService.ApplyStatistics(data, general, null, null, 512);

        Assert.Equal(42, data.PowerOnHours);
    }

    [Fact]
    public void ApplyStatistics_ReportsNothingReadWhenNoStatisticIsSupported()
    {
        var general = Page(1, (16, 5319, 0x00));

        var data = new SmartData();

        Assert.False(AtaHealthService.ApplyStatistics(data, general, null, null, 512));
        Assert.Null(data.PowerOnHours);
    }

    [Fact]
    public void ApplyStatistics_RejectsAnImplausibleTemperature()
    {
        var temperature = Page(5, (8, 9999, SupportedAndValid));

        var data = new SmartData();
        AtaHealthService.ApplyStatistics(data, null, temperature, null, 512);

        Assert.Null(data.Temperature);
    }

    [Fact]
    public void ApplyStatistics_ToleratesEveryPageBeingAbsent()
    {
        var data = new SmartData();

        Assert.False(AtaHealthService.ApplyStatistics(data, null, null, null, 512));
    }

    /// <summary>Builds a 512-byte Device Statistics page with the given entries at the given offsets.</summary>
    private static byte[] Page(int pageNumber, params (int Offset, long Value, byte Flags)[] entries)
    {
        var page = new byte[512];
        page[0] = 1;                  // revision
        page[2] = (byte)pageNumber;

        foreach (var (offset, value, flags) in entries)
        {
            for (var i = 0; i < 7; i++)
                page[offset + i] = (byte)((value >> (i * 8)) & 0xFF);
            page[offset + 7] = flags;
        }

        return page;
    }
}
