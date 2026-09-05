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
