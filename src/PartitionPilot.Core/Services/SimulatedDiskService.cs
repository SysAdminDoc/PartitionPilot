namespace PartitionPilot;

public class SimulatedDiskService : IWmiDiskService
{
    public Task<List<DiskInfo>> GetDisksAsync() => Task.FromResult(new List<DiskInfo>
    {
        new() { Number = 0, FriendlyName = "Samsung SSD 990 PRO 2TB", Size = 2_000_398_934_016, PartitionStyle = "GPT", LargestFreeExtent = 0, NumberOfPartitions = 4 },
        new() { Number = 1, FriendlyName = "WD Blue SN580 1TB", Size = 1_000_204_886_016, PartitionStyle = "GPT", LargestFreeExtent = 107_374_182_400, NumberOfPartitions = 2 },
        new() { Number = 2, FriendlyName = "Seagate Barracuda 4TB", Size = 4_000_787_030_016, PartitionStyle = "GPT", LargestFreeExtent = 0, NumberOfPartitions = 1 },
        new() { Number = 3, FriendlyName = "USB Flash Drive", Size = 32_017_047_552, PartitionStyle = "MBR", LargestFreeExtent = 0, NumberOfPartitions = 1 },
    });

    public Task<List<PartitionInfo>> GetPartitionsAsync(int diskNumber) => Task.FromResult(diskNumber switch
    {
        0 => new List<PartitionInfo>
        {
            new() { PartitionNumber = 1, Size = 104_857_600, Offset = 1_048_576, Type = "System", DiskNumber = 0 },
            new() { PartitionNumber = 2, Size = 16_777_216, Offset = 105_906_176, Type = "Reserved", DiskNumber = 0 },
            new() { PartitionNumber = 3, DriveLetter = 'C', Size = 1_932_735_283_200, Offset = 122_683_392, Type = "Basic", Label = "Windows", FileSystem = "NTFS", IsBoot = true, IsSystem = true, DiskNumber = 0 },
            new() { PartitionNumber = 4, Size = 67_108_864_000, Offset = 1_932_857_966_592, Type = "Recovery", DiskNumber = 0 },
        },
        1 => new List<PartitionInfo>
        {
            new() { PartitionNumber = 1, DriveLetter = 'D', Size = 858_993_459_200, Offset = 1_048_576, Type = "Basic", Label = "Data", FileSystem = "NTFS", DiskNumber = 1, FreeSpace = 429_496_729_600 },
            new() { PartitionNumber = 2, DriveLetter = 'E', Size = 34_359_738_368, Offset = 858_994_507_776, Type = "Basic", Label = "DevDrive", FileSystem = "ReFS", DiskNumber = 1 },
        },
        2 => new List<PartitionInfo>
        {
            new() { PartitionNumber = 1, DriveLetter = 'F', Size = 4_000_785_981_440, Offset = 1_048_576, Type = "Basic", Label = "Archive", FileSystem = "NTFS", DiskNumber = 2, FreeSpace = 1_200_000_000_000 },
        },
        3 => new List<PartitionInfo>
        {
            new() { PartitionNumber = 1, DriveLetter = 'G', Size = 32_015_998_976, Offset = 1_048_576, Type = "Basic", Label = "USB", FileSystem = "FAT32", DiskNumber = 3 },
        },
        _ => new List<PartitionInfo>()
    });

    public Task<List<VolumeInfo>> GetVolumesAsync() => Task.FromResult(new List<VolumeInfo>
    {
        new() { DriveLetter = 'C', FileSystemLabel = "Windows", FileSystemType = "NTFS", Size = 1_932_735_283_200, SizeRemaining = 483_183_820_800 },
        new() { DriveLetter = 'D', FileSystemLabel = "Data", FileSystemType = "NTFS", Size = 858_993_459_200, SizeRemaining = 429_496_729_600 },
        new() { DriveLetter = 'E', FileSystemLabel = "DevDrive", FileSystemType = "ReFS", Size = 34_359_738_368, SizeRemaining = 30_000_000_000 },
        new() { DriveLetter = 'F', FileSystemLabel = "Archive", FileSystemType = "NTFS", Size = 4_000_785_981_440, SizeRemaining = 1_200_000_000_000 },
        new() { DriveLetter = 'G', FileSystemLabel = "USB", FileSystemType = "FAT32", Size = 32_015_998_976, SizeRemaining = 28_000_000_000 },
    });

    public Task<List<PhysicalDiskInfo>> GetPhysicalDisksAsync() => Task.FromResult(new List<PhysicalDiskInfo>
    {
        new() { DeviceId = "0", FriendlyName = "Samsung SSD 990 PRO 2TB", SerialNumber = "[sim]", FirmwareVersion = "4B2QJXD7", Size = 2_000_398_934_016, LogicalSectorSize = 512, PhysicalSectorSize = 512, HealthStatus = "Healthy", MediaType = "SSD", BusType = "NVMe" },
        new() { DeviceId = "1", FriendlyName = "WD Blue SN580 1TB", SerialNumber = "[sim]", FirmwareVersion = "234113WD", Size = 1_000_204_886_016, LogicalSectorSize = 512, PhysicalSectorSize = 512, HealthStatus = "Healthy", MediaType = "SSD", BusType = "NVMe" },
        new() { DeviceId = "2", FriendlyName = "Seagate Barracuda 4TB", SerialNumber = "[sim]", FirmwareVersion = "SC60", Size = 4_000_787_030_016, LogicalSectorSize = 512, PhysicalSectorSize = 4096, HealthStatus = "Healthy", MediaType = "HDD", BusType = "SATA" },
        new() { DeviceId = "3", FriendlyName = "USB Flash Drive", SerialNumber = "[sim]", FirmwareVersion = "1.00", Size = 32_017_047_552, LogicalSectorSize = 512, PhysicalSectorSize = 512, HealthStatus = "Healthy", MediaType = "SSD", BusType = "USB" },
    });

    public Task<SmartData?> GetSmartDataAsync(string deviceId) => Task.FromResult<SmartData?>(deviceId switch
    {
        "0" => new SmartData { Temperature = 42, Wear = 3, PowerOnHours = 8760, PowerCycleCount = 245, TotalBytesWritten = 12_000_000_000_000, TotalBytesRead = 45_000_000_000_000, NvmeAvailableSpare = 100, ReallocatedSectors = 0, PendingSectors = 0 },
        "1" => new SmartData { Temperature = 38, Wear = 7, PowerOnHours = 4380, PowerCycleCount = 122, TotalBytesWritten = 5_000_000_000_000, NvmeAvailableSpare = 100, ReallocatedSectors = 0, PendingSectors = 0 },
        "2" => new SmartData { Temperature = 34, PowerOnHours = 26280, PowerCycleCount = 890, ReallocatedSectors = 2, PendingSectors = 0, ReadErrorsTotal = 4, ReadErrorsCorrected = 4 },
        _ => null
    });

    public Task<List<AlignmentInfo>> GetAlignmentAuditAsync() => Task.FromResult(new List<AlignmentInfo>
    {
        new() { DiskNumber = 0, PartitionNumber = 1, Offset = 1_048_576, IsAligned = true, Status = "Aligned (4K)" },
        new() { DiskNumber = 0, PartitionNumber = 3, DriveLetter = 'C', Offset = 122_683_392, IsAligned = true, Status = "Aligned (4K)" },
        new() { DiskNumber = 1, PartitionNumber = 1, DriveLetter = 'D', Offset = 1_048_576, IsAligned = true, Status = "Aligned (4K)" },
        new() { DiskNumber = 2, PartitionNumber = 1, DriveLetter = 'F', Offset = 1_048_576, IsAligned = true, Status = "Aligned (4K)" },
    });

    public Task<HashSet<char>> GetPagefileLocationsAsync() => Task.FromResult(new HashSet<char> { 'C' });

    public Task<List<char>> GetAvailableLettersAsync() => Task.FromResult(
        Enumerable.Range('H', 'Z' - 'H' + 1).Select(c => (char)c).ToList());

    public Task<(long Min, long Max)> GetPartitionSupportedSizeAsync(char driveLetter) =>
        Task.FromResult((Min: 52_428_800L, Max: 1_932_735_283_200L));

    public Task<List<MountedImageInfo>> GetMountedImagesAsync() => Task.FromResult(new List<MountedImageInfo>());

    public Task<Dictionary<char, string>> GetBitLockerStatusAsync() => Task.FromResult(new Dictionary<char, string>
    {
        ['C'] = "BitLocker: On (Unlocked)"
    });

    public Task<List<string>> GetBitLockerProtectedTargetsAsync(int diskNumber) =>
        Task.FromResult(new List<string>());

    public Task<Dictionary<int, string>> GetStoragePoolMembershipAsync() =>
        Task.FromResult(new Dictionary<int, string>());
}
