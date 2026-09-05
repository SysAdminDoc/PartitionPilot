using System.Runtime.InteropServices;

namespace PartitionPilot.Tests;

public class NvmeHealthServiceTests
{
    private const int DescriptorHeaderSize = 8;   // Version (4) + Size (4)
    private const int ProtocolSpecificSize = 40;  // STORAGE_PROTOCOL_SPECIFIC_DATA
    private const int HealthLogLength = 512;

    [Fact]
    public void TryLocateHealthLog_ReadsTheOffsetAndLengthTheDriverReported()
    {
        // The descriptor's own Size field reports the header length, not the payload length, so the
        // payload has to be found through ProtocolDataOffset/Length rather than assumed.
        using var buffer = DescriptorBuffer(protocolDataOffset: ProtocolSpecificSize, protocolDataLength: HealthLogLength);

        var located = NvmeHealthService.TryLocateHealthLog(
            buffer.Pointer, buffer.Length, ProtocolSpecificSize, out var offset, out var length);

        Assert.True(located);
        Assert.Equal(DescriptorHeaderSize + ProtocolSpecificSize, offset);
        Assert.Equal(HealthLogLength, length);
    }

    [Fact]
    public void TryLocateHealthLog_HonoursADriverThatPlacesThePayloadFurtherOut()
    {
        using var buffer = DescriptorBuffer(protocolDataOffset: 64, protocolDataLength: HealthLogLength);

        Assert.True(NvmeHealthService.TryLocateHealthLog(
            buffer.Pointer, buffer.Length, ProtocolSpecificSize, out var offset, out _));

        Assert.Equal(DescriptorHeaderSize + 64, offset);
    }

    [Theory]
    [InlineData(0, HealthLogLength)]                 // offset inside the descriptor itself
    [InlineData(ProtocolSpecificSize - 1, HealthLogLength)]
    [InlineData(ProtocolSpecificSize, 0)]            // no payload
    [InlineData(ProtocolSpecificSize, -1)]
    public void TryLocateHealthLog_RejectsADescriptorThatDoesNotDescribeAPayload(int protocolDataOffset, int protocolDataLength)
    {
        using var buffer = DescriptorBuffer(protocolDataOffset, protocolDataLength);

        Assert.False(NvmeHealthService.TryLocateHealthLog(
            buffer.Pointer, buffer.Length, ProtocolSpecificSize, out _, out _));
    }

    [Fact]
    public void TryLocateHealthLog_RejectsAPayloadThatRunsPastWhatTheDriverReturned()
    {
        using var buffer = DescriptorBuffer(protocolDataOffset: ProtocolSpecificSize, protocolDataLength: HealthLogLength);

        // The driver claims 512 bytes of log but only returned enough for the header.
        Assert.False(NvmeHealthService.TryLocateHealthLog(
            buffer.Pointer, DescriptorHeaderSize + ProtocolSpecificSize, ProtocolSpecificSize, out _, out _));
    }

    [Fact]
    public void TryLocateHealthLog_RejectsATruncatedReply()
    {
        using var buffer = DescriptorBuffer(protocolDataOffset: ProtocolSpecificSize, protocolDataLength: HealthLogLength);

        Assert.False(NvmeHealthService.TryLocateHealthLog(
            buffer.Pointer, DescriptorHeaderSize, ProtocolSpecificSize, out _, out _));
    }

    [Fact]
    public void ParseHealthLog_ReadsEveryFieldFromItsSpecifiedOffset()
    {
        // Offsets from NVME_HEALTH_INFO_LOG. Each field gets a distinct value so a swapped pair fails.
        var log = new byte[512];
        log[0] = 0x01;                                        // critical warning
        BitConverter.TryWriteBytes(log.AsSpan(1), (ushort)310); // composite temperature, Kelvin
        log[3] = 97;                                          // available spare %
        log[5] = 4;                                           // percentage used
        WriteCounter(log, 0x20, 1_000);                       // data units read
        WriteCounter(log, 0x30, 2_000);                       // data units written
        WriteCounter(log, 0x60, 11);                          // controller busy minutes
        WriteCounter(log, 0x70, 361);                         // power cycles
        WriteCounter(log, 0x80, 6_261);                       // power-on hours
        WriteCounter(log, 0x90, 97);                          // unsafe shutdowns
        WriteCounter(log, 0xA0, 3);                           // media errors
        WriteCounter(log, 0xB0, 5);                           // error log entries

        var data = new SmartData();
        NvmeHealthService.ParseHealthLog(data, log);

        Assert.Equal((byte)0x01, data.NvmeCriticalWarning);
        Assert.Equal(310 - 273, data.Temperature);
        Assert.Equal(97, data.NvmeAvailableSpare);
        Assert.Equal(4, data.Wear);
        Assert.Equal(1_000L * 512 * 1000, data.TotalBytesRead);
        Assert.Equal(2_000L * 512 * 1000, data.TotalBytesWritten);
        Assert.Equal(11, data.NvmeControllerBusyMinutes);
        Assert.Equal(361, data.PowerCycleCount);
        Assert.Equal(6_261, data.PowerOnHours);
        Assert.Equal(97, data.NvmeUnsafeShutdowns);
        Assert.Equal(3, data.NvmeMediaErrors);
        Assert.Equal(5, data.NvmeErrorLogEntries);
    }

    [Fact]
    public void ParseHealthLog_ReportsZeroPercentUsedOnAHealthyNewDrive()
    {
        // A brand-new drive legitimately reports 0% used. Skipping it would hide the field on exactly the
        // drives in the best condition.
        var log = new byte[512];
        log[5] = 0;
        WriteCounter(log, 0x80, 3);

        var data = new SmartData();
        NvmeHealthService.ParseHealthLog(data, log);

        Assert.Equal(0, data.Wear);
    }

    [Fact]
    public void IsEmptyLog_TreatsAnAllZeroPageAsNoAnswer()
    {
        Assert.True(NvmeHealthService.IsEmptyLog(new byte[512]));

        var populated = new byte[512];
        populated[5] = 1;
        Assert.False(NvmeHealthService.IsEmptyLog(populated));
    }

    private static void WriteCounter(byte[] log, int offset, long value) =>
        BitConverter.TryWriteBytes(log.AsSpan(offset), (ulong)value);

    private static UnmanagedBuffer DescriptorBuffer(int protocolDataOffset, int protocolDataLength)
    {
        // Sized for wherever the driver says the payload starts, so the bounds check is exercised by the
        // descriptor's contents rather than by an undersized fixture.
        var size = DescriptorHeaderSize + Math.Max(protocolDataOffset, ProtocolSpecificSize) + HealthLogLength;
        var bytes = new byte[size];

        BitConverter.TryWriteBytes(bytes.AsSpan(0), 1u);                       // Version
        BitConverter.TryWriteBytes(bytes.AsSpan(4), (uint)DescriptorHeaderSize); // Size: header only, not the payload

        var specific = DescriptorHeaderSize;
        BitConverter.TryWriteBytes(bytes.AsSpan(specific + 0), 3);   // ProtocolType = NVMe
        BitConverter.TryWriteBytes(bytes.AsSpan(specific + 4), 2);   // DataType = LogPage
        BitConverter.TryWriteBytes(bytes.AsSpan(specific + 8), 2);   // request value = health log 02h
        BitConverter.TryWriteBytes(bytes.AsSpan(specific + 16), protocolDataOffset);
        BitConverter.TryWriteBytes(bytes.AsSpan(specific + 20), protocolDataLength);

        return new UnmanagedBuffer(bytes);
    }

    private sealed class UnmanagedBuffer : IDisposable
    {
        public IntPtr Pointer { get; }
        public int Length { get; }

        public UnmanagedBuffer(byte[] bytes)
        {
            Length = bytes.Length;
            Pointer = Marshal.AllocHGlobal(Length);
            Marshal.Copy(bytes, 0, Pointer, Length);
        }

        public void Dispose() => Marshal.FreeHGlobal(Pointer);
    }
}
