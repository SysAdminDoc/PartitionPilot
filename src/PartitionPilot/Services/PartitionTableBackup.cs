using System.IO;
using System.Text.Json;

namespace PartitionPilot;

public class PartitionTableBackup
{
    private readonly WmiDiskService _wmiService;
    private readonly ActivityLog _log;
    private static readonly string BackupDir = ResolveBackupDir();

    private static string ResolveBackupDir()
    {
        var exeDir = AppContext.BaseDirectory;
        var portableMarker = Path.Combine(exeDir, "portable.txt");
        if (File.Exists(portableMarker))
            return Path.Combine(exeDir, "backups");
        return Path.Combine(Path.GetTempPath(), "PartitionPilot", "backups");
    }

    public PartitionTableBackup(WmiDiskService wmiService, ActivityLog log)
    {
        _wmiService = wmiService;
        _log = log;
    }

    public async Task SaveSnapshotAsync(int diskNumber)
    {
        try
        {
            var partitions = await _wmiService.GetPartitionsAsync(diskNumber);
            var disks = await _wmiService.GetDisksAsync();
            var disk = disks.FirstOrDefault(d => d.Number == diskNumber);

            var snapshot = new
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                DiskNumber = diskNumber,
                DiskName = disk?.FriendlyName ?? "Unknown",
                DiskSize = disk?.Size ?? 0,
                PartitionStyle = disk?.PartitionStyle ?? "Unknown",
                Partitions = partitions.Select(p => new
                {
                    p.PartitionNumber,
                    DriveLetter = p.DriveLetter?.ToString(),
                    p.Label,
                    p.Size,
                    p.Offset,
                    p.Type,
                    p.FileSystem,
                    p.IsBoot,
                    p.IsSystem,
                    p.IsActive,
                    p.IsHidden
                }).ToArray()
            };

            Directory.CreateDirectory(BackupDir);
            var filename = $"disk{diskNumber}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            var path = Path.Combine(BackupDir, filename);
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);

            _log.Log($"Partition table snapshot saved: {filename}");

            PurgeOldSnapshots();
        }
        catch (Exception ex)
        {
            _log.Log($"Could not save partition table snapshot: {ex.Message}");
        }
    }

    private static void PurgeOldSnapshots()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-30);
            foreach (var file in Directory.EnumerateFiles(BackupDir, "*.json"))
            {
                if (File.GetCreationTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch { }
    }
}
