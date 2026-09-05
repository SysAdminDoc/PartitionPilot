using System.IO;

namespace PartitionPilot.Tests;

public class GptRepairServiceTests
{
    private const int SectorSize = 512;
    private const int EntryCount = 128;
    private const int EntrySize = 128;
    private const int EntrySectors = EntryCount * EntrySize / SectorSize; // 32

    [Fact]
    public void RelocateBackupHeader_MovesTheBackupToTheDestinationsLastLba()
    {
        // A 256-sector source cloned onto a 1024-sector destination: the backup GPT is stranded at
        // LBA 255 and the primary still points there.
        var disk = SyntheticGptDisk(destinationSectors: 1024, sourceSectors: 256);

        var result = GptRepairService.RelocateBackupHeader(disk, SectorSize, 1024, 256);

        Assert.Equal(GptRepairOutcome.Repaired, result.Outcome);

        var primary = ReadSector(disk, 1);
        Assert.Equal(1023, BitConverter.ToInt64(primary, 0x20));       // AlternateLBA
        Assert.Equal(1023 - EntrySectors - 1, BitConverter.ToInt64(primary, 0x30)); // LastUsableLBA
        Assert.True(HeaderCrcIsValid(primary));

        var backup = ReadSector(disk, 1023);
        Assert.Equal("EFI PART", System.Text.Encoding.ASCII.GetString(backup, 0, 8));
        Assert.Equal(1023, BitConverter.ToInt64(backup, 0x18));        // MyLBA
        Assert.Equal(1, BitConverter.ToInt64(backup, 0x20));           // AlternateLBA
        Assert.Equal(1023 - EntrySectors, BitConverter.ToInt64(backup, 0x48)); // PartitionEntryLBA
        Assert.True(HeaderCrcIsValid(backup));
    }

    [Fact]
    public void RelocateBackupHeader_ErasesTheStrandedBackupHeader()
    {
        var disk = SyntheticGptDisk(destinationSectors: 1024, sourceSectors: 256);
        Assert.Equal("EFI PART", System.Text.Encoding.ASCII.GetString(ReadSector(disk, 255), 0, 8));

        GptRepairService.RelocateBackupHeader(disk, SectorSize, 1024, 256);

        Assert.All(ReadSector(disk, 255), b => Assert.Equal(0, b));
    }

    [Fact]
    public void RelocateBackupHeader_CopiesThePartitionEntryArrayToTheNewBackupLocation()
    {
        var disk = SyntheticGptDisk(destinationSectors: 1024, sourceSectors: 256);
        var primaryEntries = ReadSectors(disk, 2, EntrySectors);

        GptRepairService.RelocateBackupHeader(disk, SectorSize, 1024, 256);

        Assert.Equal(primaryEntries, ReadSectors(disk, 1023 - EntrySectors, EntrySectors));
    }

    [Fact]
    public void RelocateBackupHeader_WidensTheProtectiveMbr()
    {
        var disk = SyntheticGptDisk(destinationSectors: 1024, sourceSectors: 256);
        Assert.Equal(255u, BitConverter.ToUInt32(ReadSector(disk, 0), 0x1BE + 12));

        GptRepairService.RelocateBackupHeader(disk, SectorSize, 1024, 256);

        Assert.Equal(1023u, BitConverter.ToUInt32(ReadSector(disk, 0), 0x1BE + 12));
    }

    [Fact]
    public void RelocateBackupHeader_IsIdempotent()
    {
        var disk = SyntheticGptDisk(destinationSectors: 1024, sourceSectors: 256);

        Assert.Equal(GptRepairOutcome.Repaired,
            GptRepairService.RelocateBackupHeader(disk, SectorSize, 1024, 256).Outcome);

        var afterFirst = disk.ToArray();
        var second = GptRepairService.RelocateBackupHeader(disk, SectorSize, 1024, 1024);

        Assert.Equal(GptRepairOutcome.AlreadyCorrect, second.Outcome);
        Assert.Equal(afterFirst, disk.ToArray());
    }

    [Fact]
    public void RelocateBackupHeader_LeavesAnMbrDiskAlone()
    {
        var disk = new MemoryStream(new byte[1024 * SectorSize]);
        var before = disk.ToArray();

        var result = GptRepairService.RelocateBackupHeader(disk, SectorSize, 1024, 256);

        Assert.Equal(GptRepairOutcome.NotGpt, result.Outcome);
        Assert.Equal(before, disk.ToArray());
    }

    [Fact]
    public void RelocateBackupHeader_RefusesToRewriteAHeaderThatFailsItsOwnCrc()
    {
        var disk = SyntheticGptDisk(destinationSectors: 1024, sourceSectors: 256);
        disk.Seek(1 * SectorSize + 0x38, SeekOrigin.Begin);
        disk.WriteByte(0xAA); // corrupt DiskGUID without restamping the header CRC
        var before = disk.ToArray();

        var result = GptRepairService.RelocateBackupHeader(disk, SectorSize, 1024, 256);

        Assert.Equal(GptRepairOutcome.PrimaryHeaderInvalid, result.Outcome);
        Assert.Equal(before, disk.ToArray());
    }

    [Fact]
    public void Crc32_MatchesTheKnownIeeeCheckValue()
    {
        // The standard CRC-32 check value for the ASCII string "123456789".
        Assert.Equal(0xCBF43926u, GptRepairService.Crc32("123456789"u8));
    }

    private static bool HeaderCrcIsValid(byte[] header)
    {
        var headerSize = (int)BitConverter.ToUInt32(header, 0x0C);
        var stored = BitConverter.ToUInt32(header, 0x10);
        var scratch = header.AsSpan(0, headerSize).ToArray();
        scratch.AsSpan(0x10, 4).Clear();
        return GptRepairService.Crc32(scratch) == stored;
    }

    private static byte[] ReadSector(MemoryStream disk, long lba) => ReadSectors(disk, lba, 1);

    private static byte[] ReadSectors(MemoryStream disk, long lba, long count)
    {
        var buffer = new byte[count * SectorSize];
        disk.Seek(lba * SectorSize, SeekOrigin.Begin);
        disk.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>
    /// Builds the disk a sector clone leaves behind: a valid GPT sized for <paramref name="sourceSectors"/>
    /// written onto a larger <paramref name="destinationSectors"/> device, so the backup header sits mid-disk.
    /// </summary>
    private static MemoryStream SyntheticGptDisk(long destinationSectors, long sourceSectors)
    {
        var image = new byte[destinationSectors * SectorSize];
        var sourceLastLba = sourceSectors - 1;
        var sourceBackupEntryLba = sourceLastLba - EntrySectors;

        // Protective MBR describing the source disk.
        image[0x1BE + 4] = 0xEE;
        BitConverter.TryWriteBytes(image.AsSpan(0x1BE + 8), 1u);
        BitConverter.TryWriteBytes(image.AsSpan(0x1BE + 12), (uint)sourceLastLba);
        image[510] = 0x55;
        image[511] = 0xAA;

        // Partition entry array: one entry so its CRC is not trivially zero.
        var entries = new byte[EntrySectors * SectorSize];
        for (var i = 0; i < 16; i++)
            entries[i] = (byte)(i + 1); // PartitionTypeGUID
        BitConverter.TryWriteBytes(entries.AsSpan(32), 34L);                        // StartingLBA
        BitConverter.TryWriteBytes(entries.AsSpan(40), sourceBackupEntryLba - 1);   // EndingLBA
        entries.CopyTo(image, 2 * SectorSize);
        entries.CopyTo(image, sourceBackupEntryLba * SectorSize);
        var entriesCrc = GptRepairService.Crc32(entries.AsSpan(0, EntryCount * EntrySize));

        var primary = BuildHeader(
            myLba: 1, alternateLba: sourceLastLba, entryLba: 2,
            lastUsableLba: sourceBackupEntryLba - 1, entriesCrc: entriesCrc);
        primary.CopyTo(image, 1 * SectorSize);

        var backup = BuildHeader(
            myLba: sourceLastLba, alternateLba: 1, entryLba: sourceBackupEntryLba,
            lastUsableLba: sourceBackupEntryLba - 1, entriesCrc: entriesCrc);
        backup.CopyTo(image, sourceLastLba * SectorSize);

        return new MemoryStream(image) { Position = 0 };
    }

    private static byte[] BuildHeader(long myLba, long alternateLba, long entryLba, long lastUsableLba, uint entriesCrc)
    {
        var header = new byte[SectorSize];
        "EFI PART"u8.CopyTo(header);
        BitConverter.TryWriteBytes(header.AsSpan(0x08), 0x00010000u); // Revision 1.0
        BitConverter.TryWriteBytes(header.AsSpan(0x0C), 92u);         // HeaderSize
        BitConverter.TryWriteBytes(header.AsSpan(0x18), myLba);
        BitConverter.TryWriteBytes(header.AsSpan(0x20), alternateLba);
        BitConverter.TryWriteBytes(header.AsSpan(0x28), 34L);         // FirstUsableLBA
        BitConverter.TryWriteBytes(header.AsSpan(0x30), lastUsableLba);
        Guid.Parse("7f8d3a1e-4c22-4f6b-9a10-b2c3d4e5f607").ToByteArray().CopyTo(header, 0x38);
        BitConverter.TryWriteBytes(header.AsSpan(0x48), entryLba);
        BitConverter.TryWriteBytes(header.AsSpan(0x50), (uint)EntryCount);
        BitConverter.TryWriteBytes(header.AsSpan(0x54), (uint)EntrySize);
        BitConverter.TryWriteBytes(header.AsSpan(0x58), entriesCrc);

        var scratch = header.AsSpan(0, 92).ToArray();
        scratch.AsSpan(0x10, 4).Clear();
        BitConverter.TryWriteBytes(header.AsSpan(0x10), GptRepairService.Crc32(scratch));
        return header;
    }
}
