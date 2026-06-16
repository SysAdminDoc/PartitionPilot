namespace PartitionPilot.Tests;

public class EnrichPartitionsTests
{
    [Fact]
    public void EnrichPartitionsWithVolumes_MatchesByDriveLetter()
    {
        var partitions = new List<PartitionInfo>
        {
            new() { DriveLetter = 'C', PartitionNumber = 1, Size = 100_000_000_000 },
            new() { DriveLetter = 'D', PartitionNumber = 2, Size = 200_000_000_000 },
            new() { DriveLetter = null, PartitionNumber = 3, Size = 500_000_000 },
        };

        var volumes = new List<VolumeInfo>
        {
            new() { DriveLetter = 'C', FileSystemLabel = "Windows", FileSystemType = "NTFS", SizeRemaining = 50_000_000_000 },
            new() { DriveLetter = 'D', FileSystemLabel = "Data", FileSystemType = "NTFS", SizeRemaining = 150_000_000_000 },
        };

        WmiDiskService.EnrichPartitionsWithVolumes(partitions, volumes);

        Assert.Equal("Windows", partitions[0].Label);
        Assert.Equal("NTFS", partitions[0].FileSystem);
        Assert.Equal(50_000_000_000, partitions[0].FreeSpace);

        Assert.Equal("Data", partitions[1].Label);
        Assert.Equal(150_000_000_000, partitions[1].FreeSpace);

        Assert.Equal("", partitions[2].Label);
        Assert.Equal("", partitions[2].FileSystem);
    }

    [Fact]
    public void EnrichPartitionsWithVolumes_HandlesEmptyLists()
    {
        var partitions = new List<PartitionInfo>();
        var volumes = new List<VolumeInfo>();

        WmiDiskService.EnrichPartitionsWithVolumes(partitions, volumes);

        Assert.Empty(partitions);
    }
}
