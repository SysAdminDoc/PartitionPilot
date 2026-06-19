namespace PartitionPilot;

public interface IWmiDiskService
{
    Task<List<DiskInfo>> GetDisksAsync();
    Task<List<PartitionInfo>> GetPartitionsAsync(int diskNumber);
    Task<List<VolumeInfo>> GetVolumesAsync();
    Task<List<PhysicalDiskInfo>> GetPhysicalDisksAsync();
    Task<SmartData?> GetSmartDataAsync(string deviceId);
    Task<List<AlignmentInfo>> GetAlignmentAuditAsync();
    Task<HashSet<char>> GetPagefileLocationsAsync();
    Task<List<char>> GetAvailableLettersAsync();
    Task<(long Min, long Max)> GetPartitionSupportedSizeAsync(char driveLetter);
    Task<List<MountedImageInfo>> GetMountedImagesAsync();
    Task<Dictionary<char, string>> GetBitLockerStatusAsync();
    Task<List<string>> GetBitLockerProtectedTargetsAsync(int diskNumber);
}
