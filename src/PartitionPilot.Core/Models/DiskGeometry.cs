namespace PartitionPilot;

public static class DiskGeometry
{
    public const int DefaultLogicalSectorSize = 512;
    private const int MaximumSupportedLogicalSectorSize = 64 * 1024;

    public static int NormalizeLogicalSectorSize(int logicalSectorSize)
    {
        return logicalSectorSize is >= DefaultLogicalSectorSize and <= MaximumSupportedLogicalSectorSize
               && (logicalSectorSize & (logicalSectorSize - 1)) == 0
            ? logicalSectorSize
            : DefaultLogicalSectorSize;
    }

    public static long GetByteOffset(long sectorLba, int logicalSectorSize)
    {
        if (sectorLba < 0)
            throw new ArgumentOutOfRangeException(nameof(sectorLba));

        return checked(sectorLba * (long)NormalizeLogicalSectorSize(logicalSectorSize));
    }
}
