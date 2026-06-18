using System.IO;
using System.Text.Json;

namespace PartitionPilot;

public class PartitionTableBackup
{
    private readonly WmiDiskService _wmiService;
    private readonly ActivityLog _log;
    private static readonly string BackupDir = ResolveBackupDir();
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string BackupDirectory => BackupDir;

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
            var json = JsonSerializer.Serialize(snapshot, SnapshotJsonOptions);
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

    public async Task<List<PartitionSnapshot>> ListSnapshotsAsync()
    {
        Directory.CreateDirectory(BackupDir);
        var snapshots = new List<PartitionSnapshot>();

        foreach (var file in Directory.EnumerateFiles(BackupDir, "*.json").OrderByDescending(File.GetCreationTimeUtc))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var snapshot = JsonSerializer.Deserialize<PartitionSnapshot>(json, SnapshotJsonOptions);
                if (snapshot is null)
                    continue;

                snapshot.FilePath = file;
                snapshots.Add(snapshot);
            }
            catch (Exception ex)
            {
                _log.Log($"Could not read partition snapshot {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return snapshots
            .OrderByDescending(s => s.CapturedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(s => s.FileName)
            .ToList();
    }

    public async Task ExportSnapshotAsync(PartitionSnapshot snapshot, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(snapshot.FilePath) || !File.Exists(snapshot.FilePath))
            throw new InvalidOperationException("The selected snapshot file is no longer available.");

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        await using var source = File.OpenRead(snapshot.FilePath);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination);
    }

    public async Task<string> CompareSnapshotToCurrentAsync(PartitionSnapshot snapshot)
    {
        var current = await _wmiService.GetPartitionsAsync(snapshot.DiskNumber);
        return BuildDiff(snapshot, current);
    }

    public static string BuildDiff(PartitionSnapshot snapshot, IReadOnlyCollection<PartitionInfo> currentPartitions)
    {
        var lines = new List<string>
        {
            $"Snapshot: {snapshot.CapturedAtText}",
            $"Disk: {snapshot.DiskSummary}",
            ""
        };

        var changes = new List<string>();
        var currentByNumber = currentPartitions.ToDictionary(p => p.PartitionNumber);

        foreach (var saved in snapshot.Partitions.OrderBy(p => p.PartitionNumber))
        {
            if (!currentByNumber.TryGetValue(saved.PartitionNumber, out var current))
            {
                changes.Add($"Missing now: partition {saved.PartitionNumber} ({saved.LetterDisplay}, {saved.SizeText}, {saved.Type})");
                continue;
            }

            var changedFields = new List<string>();
            var currentLetter = current.DriveLetter?.ToString();
            if (!string.Equals(saved.DriveLetter, currentLetter, StringComparison.OrdinalIgnoreCase))
                changedFields.Add($"letter {saved.LetterDisplay} -> {(current.DriveLetter.HasValue ? $"{current.DriveLetter}:" : "-")}");
            if (saved.Size != current.Size)
                changedFields.Add($"size {SizeUtil.Format(saved.Size)} -> {SizeUtil.Format(current.Size)}");
            if (saved.Offset != current.Offset)
                changedFields.Add($"offset {SizeUtil.Format(saved.Offset)} -> {SizeUtil.Format(current.Offset)}");
            if (!string.Equals(saved.Type, current.Type, StringComparison.OrdinalIgnoreCase))
                changedFields.Add($"type {saved.Type} -> {current.Type}");

            if (changedFields.Count > 0)
                changes.Add($"Changed: partition {saved.PartitionNumber}: {string.Join(", ", changedFields)}");
        }

        var savedNumbers = snapshot.Partitions.Select(p => p.PartitionNumber).ToHashSet();
        foreach (var current in currentPartitions.OrderBy(p => p.PartitionNumber))
        {
            if (!savedNumbers.Contains(current.PartitionNumber))
                changes.Add($"New now: partition {current.PartitionNumber} ({current.LetterDisplay}, {current.SizeText}, {current.Type})");
        }

        if (changes.Count == 0)
            changes.Add("No partition-number, size, offset, letter, or type changes detected.");

        lines.AddRange(changes);
        return string.Join(Environment.NewLine, lines);
    }

    public static string BuildRecoveryCommands(PartitionSnapshot snapshot)
    {
        var lines = new List<string>
        {
            "# PartitionPilot guided recovery notes",
            "# Review these commands in an elevated terminal. They do not restore automatically.",
            $"# Snapshot: {snapshot.FileName}",
            $"# Captured: {snapshot.CapturedAtText}",
            $"# Disk: {snapshot.DiskSummary}",
            "",
            "Get-Disk -Number " + snapshot.DiskNumber,
            "Get-Partition -DiskNumber " + snapshot.DiskNumber + " | Sort-Object PartitionNumber | Format-Table -AutoSize",
            "Get-Volume | Sort-Object DriveLetter | Format-Table -AutoSize",
            "",
            "# Captured partition layout"
        };

        foreach (var partition in snapshot.Partitions.OrderBy(p => p.PartitionNumber))
        {
            lines.Add(
                $"# Partition {partition.PartitionNumber}: {partition.LetterDisplay} {partition.Type}, {partition.SizeText}, offset {partition.OffsetText}, fs={partition.FileSystem}, roles={partition.RoleText}");
        }

        lines.Add("");
        lines.Add("# If recovery is needed, compare current output to the snapshot and use Windows recovery tools or a specialist partition editor.");
        lines.Add("# PartitionPilot intentionally does not generate destructive restore commands from snapshots.");
        return string.Join(Environment.NewLine, lines);
    }
}
