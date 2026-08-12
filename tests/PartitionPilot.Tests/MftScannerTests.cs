using System.Buffers.Binary;
using System.Text;

namespace PartitionPilot.Tests;

public class MftScannerTests
{
    [Fact]
    public void TryParseUsnRecord_ParsesBoundedRecord()
    {
        var nameBytes = Encoding.Unicode.GetBytes("file.bin");
        var data = CreateRecord(60 + nameBytes.Length, nameBytes);

        var parsed = MftScanner.TryParseUsnRecord(data, out var record);

        Assert.True(parsed);
        Assert.Equal(data.Length, record.RecordLength);
        Assert.Equal("file.bin", record.Name);
        Assert.Equal(42, record.FileReference);
        Assert.Equal(5, record.ParentReference);
    }

    [Fact]
    public void TryParseUsnRecord_RejectsRecordShorterThanFixedHeader()
    {
        var data = new byte[59];
        BinaryPrimitives.WriteInt32LittleEndian(data, data.Length);

        Assert.False(MftScanner.TryParseUsnRecord(data, out _));
    }

    [Fact]
    public void TryParseUsnRecord_RejectsRecordLongerThanAvailableData()
    {
        var data = new byte[60];
        BinaryPrimitives.WriteInt32LittleEndian(data, 64);

        Assert.False(MftScanner.TryParseUsnRecord(data, out _));
    }

    [Fact]
    public void TryParseUsnRecord_RejectsFilenameOutsideRecord()
    {
        var data = CreateRecord(64, Encoding.Unicode.GetBytes("tool"));

        Assert.False(MftScanner.TryParseUsnRecord(data, out _));
    }

    private static byte[] CreateRecord(int recordLength, byte[] nameBytes)
    {
        var data = new byte[recordLength];
        BinaryPrimitives.WriteInt32LittleEndian(data, recordLength);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), 42);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(16), 5);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(40), 1024);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(56), (ushort)nameBytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(58), 60);
        nameBytes.AsSpan(0, Math.Min(nameBytes.Length, Math.Max(0, data.Length - 60)))
            .CopyTo(data.AsSpan(60));
        return data;
    }
}
