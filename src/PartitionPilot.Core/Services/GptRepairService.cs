using System.IO;

namespace PartitionPilot;

public enum GptRepairOutcome
{
    /// <summary>No GPT primary header was present — an MBR or unpartitioned disk needs no repair.</summary>
    NotGpt,

    /// <summary>The primary header failed its own CRC. Nothing was written; the disk needs manual attention.</summary>
    PrimaryHeaderInvalid,

    /// <summary>The backup header already sat at the last LBA. Nothing was written.</summary>
    AlreadyCorrect,

    /// <summary>The backup header and entry array were relocated to the end of the destination disk.</summary>
    Repaired
}

public sealed record GptRepairResult(GptRepairOutcome Outcome, string Detail)
{
    public bool IsHealthy => Outcome is GptRepairOutcome.NotGpt or GptRepairOutcome.AlreadyCorrect or GptRepairOutcome.Repaired;
}

/// <summary>
/// Repairs the GPT of a disk that received a sector-for-sector clone from a smaller source.
/// <para>
/// A raw clone copies the source byte-for-byte, so the secondary GPT header lands where the
/// <em>source</em> disk ended rather than at the destination's last LBA, the primary header's
/// <c>AlternateLBA</c> points at that stale location, and whatever secondary header the destination
/// already carried survives at its true last LBA. Windows then reports the disk as needing repair and
/// the trailing space is unusable. This moves the backup header and entry array to the end of the
/// destination, re-points the primary at them, and erases the stranded copy.
/// </para>
/// </summary>
public static class GptRepairService
{
    private const long PrimaryHeaderLba = 1;
    private const int SignatureOffset = 0x00;
    private const int HeaderSizeOffset = 0x0C;
    private const int HeaderCrcOffset = 0x10;
    private const int MyLbaOffset = 0x18;
    private const int AlternateLbaOffset = 0x20;
    private const int LastUsableLbaOffset = 0x30;
    private const int PartitionEntryLbaOffset = 0x48;
    private const int NumberOfPartitionEntriesOffset = 0x50;
    private const int SizeOfPartitionEntryOffset = 0x54;
    private const int MinimumHeaderSize = 92;

    private const int MbrFirstPartitionEntryOffset = 0x1BE;
    private const int MbrPartitionTypeOffset = 4;
    private const int MbrSizeInLbaOffset = 12;
    private const byte ProtectiveMbrPartitionType = 0xEE;

    private static ReadOnlySpan<byte> GptSignature => "EFI PART"u8;

    /// <summary>
    /// Moves the backup GPT to the end of <paramref name="disk"/>.
    /// </summary>
    /// <param name="disk">Seekable random access to the whole destination disk.</param>
    /// <param name="sectorSize">Logical sector size of the destination.</param>
    /// <param name="destinationSectorCount">Total logical sectors on the destination.</param>
    /// <param name="sourceSectorCount">
    /// Sectors copied from the source, used to locate and erase the stranded backup header.
    /// </param>
    public static GptRepairResult RelocateBackupHeader(
        Stream disk,
        int sectorSize,
        long destinationSectorCount,
        long sourceSectorCount,
        IActivityLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(disk);
        if (sectorSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(sectorSize), "Sector size must be positive.");
        if (destinationSectorCount < 3)
            throw new ArgumentOutOfRangeException(nameof(destinationSectorCount),
                "A GPT disk needs at least a protective MBR, a header and one entry sector.");

        var primary = ReadSectors(disk, sectorSize, PrimaryHeaderLba, 1);
        if (!primary.AsSpan(SignatureOffset, GptSignature.Length).SequenceEqual(GptSignature))
            return new GptRepairResult(GptRepairOutcome.NotGpt,
                "No GPT primary header at LBA 1 — nothing to repair.");

        var headerSize = (int)BitConverter.ToUInt32(primary, HeaderSizeOffset);
        if (headerSize < MinimumHeaderSize || headerSize > sectorSize)
            return new GptRepairResult(GptRepairOutcome.PrimaryHeaderInvalid,
                $"Primary GPT header declares an out-of-range header size of {headerSize} bytes.");

        if (!HeaderCrcMatches(primary, headerSize))
            return new GptRepairResult(GptRepairOutcome.PrimaryHeaderInvalid,
                "Primary GPT header failed its own CRC32 check — refusing to rewrite the partition table.");

        var entryCount = BitConverter.ToUInt32(primary, NumberOfPartitionEntriesOffset);
        var entrySize = BitConverter.ToUInt32(primary, SizeOfPartitionEntryOffset);
        var primaryEntryLba = BitConverter.ToInt64(primary, PartitionEntryLbaOffset);
        if (entryCount == 0 || entrySize == 0)
            return new GptRepairResult(GptRepairOutcome.PrimaryHeaderInvalid,
                "Primary GPT header declares an empty partition entry array.");

        var entrySectors = checked((long)((entryCount * (long)entrySize + sectorSize - 1) / sectorSize));
        var newLastLba = destinationSectorCount - 1;
        var newEntryLba = newLastLba - entrySectors;
        var newLastUsableLba = newEntryLba - 1;

        if (newEntryLba <= primaryEntryLba + entrySectors)
            return new GptRepairResult(GptRepairOutcome.PrimaryHeaderInvalid,
                "Destination is too small to hold a backup GPT clear of the primary entry array.");

        if (BitConverter.ToInt64(primary, AlternateLbaOffset) == newLastLba &&
            BitConverter.ToInt64(primary, LastUsableLbaOffset) == newLastUsableLba &&
            BackupHeaderIsValidAt(disk, sectorSize, newLastLba, destinationSectorCount, headerSize))
        {
            return new GptRepairResult(GptRepairOutcome.AlreadyCorrect,
                $"Backup GPT already sits at LBA {newLastLba}.");
        }

        var entries = ReadSectors(disk, sectorSize, primaryEntryLba, entrySectors);

        BitConverter.TryWriteBytes(primary.AsSpan(AlternateLbaOffset), newLastLba);
        BitConverter.TryWriteBytes(primary.AsSpan(LastUsableLbaOffset), newLastUsableLba);
        StampHeaderCrc(primary, headerSize);

        var backup = (byte[])primary.Clone();
        BitConverter.TryWriteBytes(backup.AsSpan(MyLbaOffset), newLastLba);
        BitConverter.TryWriteBytes(backup.AsSpan(AlternateLbaOffset), PrimaryHeaderLba);
        BitConverter.TryWriteBytes(backup.AsSpan(PartitionEntryLbaOffset), newEntryLba);
        StampHeaderCrc(backup, headerSize);

        WriteSectors(disk, sectorSize, newEntryLba, entries);
        WriteSectors(disk, sectorSize, newLastLba, backup);
        WriteSectors(disk, sectorSize, PrimaryHeaderLba, primary);

        var strandedLba = sourceSectorCount - 1;
        var erasedStranded = false;
        if (strandedLba > PrimaryHeaderLba && strandedLba < newLastLba && strandedLba < destinationSectorCount)
        {
            var stranded = ReadSectors(disk, sectorSize, strandedLba, 1);
            if (stranded.AsSpan(SignatureOffset, GptSignature.Length).SequenceEqual(GptSignature))
            {
                WriteSectors(disk, sectorSize, strandedLba, new byte[sectorSize]);
                erasedStranded = true;
            }
        }

        UpdateProtectiveMbr(disk, sectorSize, destinationSectorCount);
        disk.Flush();

        var detail =
            $"Backup GPT relocated to LBA {newLastLba} (entries at {newEntryLba}, last usable LBA {newLastUsableLba})" +
            (erasedStranded ? $"; erased the stranded backup header at LBA {strandedLba}." : ".");
        log?.Log($"GPT repair: {detail}");
        return new GptRepairResult(GptRepairOutcome.Repaired, detail);
    }

    private static bool BackupHeaderIsValidAt(
        Stream disk, int sectorSize, long lba, long destinationSectorCount, int headerSize)
    {
        // Bounds come from the caller's sector count, never from Stream.Length: GetFileSizeEx is not
        // reliable against a raw \\.\PhysicalDriveN handle, and reading it here would throw on the one
        // path that runs when the disk already needs no repair.
        if (lba < 0 || lba >= destinationSectorCount)
            return false;

        var candidate = ReadSectors(disk, sectorSize, lba, 1);
        return candidate.AsSpan(SignatureOffset, GptSignature.Length).SequenceEqual(GptSignature) &&
               HeaderCrcMatches(candidate, headerSize) &&
               BitConverter.ToInt64(candidate, MyLbaOffset) == lba;
    }

    /// <summary>
    /// Widens the protective MBR entry to cover the destination. Left stale, it describes the smaller
    /// source disk and tools reading the MBR see phantom free space past its end.
    /// </summary>
    private static void UpdateProtectiveMbr(Stream disk, int sectorSize, long destinationSectorCount)
    {
        var mbr = ReadSectors(disk, sectorSize, 0, 1);
        var entry = MbrFirstPartitionEntryOffset;
        if (mbr[entry + MbrPartitionTypeOffset] != ProtectiveMbrPartitionType)
            return;

        var sizeInLba = (uint)Math.Min(destinationSectorCount - 1, uint.MaxValue);
        if (BitConverter.ToUInt32(mbr, entry + MbrSizeInLbaOffset) == sizeInLba)
            return;

        BitConverter.TryWriteBytes(mbr.AsSpan(entry + MbrSizeInLbaOffset), sizeInLba);
        WriteSectors(disk, sectorSize, 0, mbr);
    }

    private static bool HeaderCrcMatches(byte[] header, int headerSize) =>
        BitConverter.ToUInt32(header, HeaderCrcOffset) == ComputeHeaderCrc(header, headerSize);

    private static void StampHeaderCrc(byte[] header, int headerSize) =>
        BitConverter.TryWriteBytes(header.AsSpan(HeaderCrcOffset), ComputeHeaderCrc(header, headerSize));

    private static uint ComputeHeaderCrc(byte[] header, int headerSize)
    {
        var scratch = new byte[headerSize];
        header.AsSpan(0, headerSize).CopyTo(scratch);
        scratch.AsSpan(HeaderCrcOffset, sizeof(uint)).Clear();
        return Crc32(scratch);
    }

    /// <summary>CRC-32 as the UEFI specification defines it for GPT structures (IEEE 802.3, reflected).</summary>
    internal static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)(-(int)(crc & 1)));
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static byte[] ReadSectors(Stream disk, int sectorSize, long lba, long sectorCount)
    {
        var buffer = new byte[checked((int)(sectorCount * sectorSize))];
        disk.Seek(lba * (long)sectorSize, SeekOrigin.Begin);
        disk.ReadExactly(buffer);
        return buffer;
    }

    private static void WriteSectors(Stream disk, int sectorSize, long lba, byte[] data)
    {
        if (data.Length % sectorSize != 0)
            throw new ArgumentException("Disk writes must cover whole sectors.", nameof(data));

        disk.Seek(lba * (long)sectorSize, SeekOrigin.Begin);
        disk.Write(data, 0, data.Length);
    }
}
