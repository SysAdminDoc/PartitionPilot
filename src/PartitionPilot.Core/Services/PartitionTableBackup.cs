using System.IO;
using System.Text.Json;

namespace PartitionPilot;

public class PartitionTableBackup
{
    private readonly IWmiDiskService _wmiService;
    private readonly IActivityLog _log;
    private readonly string _backupDir;
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
        var programData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PartitionPilot", "backups");
        if (Directory.Exists(programData))
            return programData;
        try
        {
            Directory.CreateDirectory(programData);
            return programData;
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), "PartitionPilot", "backups");
        }
    }

    public PartitionTableBackup(IWmiDiskService wmiService, IActivityLog log, string? backupDirectory = null)
    {
        _wmiService = wmiService;
        _log = log;
        _backupDir = backupDirectory ?? BackupDir;
    }

    private const int CurrentSchemaVersion = 2;

    public async Task SaveSnapshotAsync(int diskNumber)
    {
        await SaveSnapshotCoreAsync(diskNumber, operationName: null, requireSuccess: false);
    }

    public async Task<string> SaveSnapshotForDestructiveOperationAsync(
        int diskNumber,
        string operationName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(operationName))
            throw new ArgumentException("Operation name is required.", nameof(operationName));

        return await SaveSnapshotCoreAsync(diskNumber, operationName, requireSuccess: true, ct)
            ?? throw new InvalidOperationException("Could not save the required partition table snapshot.");
    }

    private async Task<string?> SaveSnapshotCoreAsync(
        int diskNumber,
        string? operationName,
        bool requireSuccess,
        CancellationToken ct = default)
    {
        string? path = null;
        try
        {
            var partitions = await _wmiService.GetPartitionsAsync(diskNumber);
            var disks = await _wmiService.GetDisksAsync();
            var disk = disks.FirstOrDefault(d => d.Number == diskNumber);

            var snapshot = new
            {
                SchemaVersion = CurrentSchemaVersion,
                Timestamp = DateTime.UtcNow.ToString("o"),
                DiskNumber = diskNumber,
                DiskName = disk?.FriendlyName ?? "Unknown",
                DiskSize = disk?.Size ?? 0,
                PartitionStyle = disk?.PartitionStyle ?? "Unknown",
                DiskIdentity = disk?.ToIdentitySnapshot(),
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

            Directory.CreateDirectory(_backupDir);
            var suffix = string.IsNullOrWhiteSpace(operationName)
                ? ""
                : $"_{SanitizeFileSuffix(operationName)}";
            var filename = $"disk{diskNumber}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}{suffix}.json";
            path = Path.Combine(_backupDir, filename);
            var json = JsonSerializer.Serialize(snapshot, SnapshotJsonOptions);
            await File.WriteAllTextAsync(path, json, ct);

            if (string.IsNullOrWhiteSpace(operationName))
                _log.Log($"Partition table snapshot saved: {filename}");
            else
                _log.Log($"Pre-destruction snapshot saved before {operationName} on Disk {diskNumber}: {path}");

            PurgeOldSnapshots(_backupDir);
            return path;
        }
        catch (Exception ex)
        {
            if (!requireSuccess)
            {
                _log.Log($"Could not save partition table snapshot: {ex.Message}");
                return null;
            }

            var target = path ?? _backupDir;
            var message =
                $"Could not save required pre-destruction partition snapshot for {operationName} on Disk {diskNumber} at {target}: {ex.Message}. Destructive operation blocked before disk changes.";
            _log.Log(message);
            throw new InvalidOperationException(message, ex);
        }
    }

    private static void PurgeOldSnapshots(string backupDir)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-30);
            foreach (var file in Directory.EnumerateFiles(backupDir, "*.json"))
            {
                if (File.GetCreationTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch { }
    }

    private static string SanitizeFileSuffix(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();
        var suffix = new string(chars).Trim('_');
        while (suffix.Contains("__", StringComparison.Ordinal))
            suffix = suffix.Replace("__", "_", StringComparison.Ordinal);
        return string.IsNullOrEmpty(suffix) ? "destructive_operation" : suffix;
    }

    public async Task<List<PartitionSnapshot>> ListSnapshotsAsync()
    {
        Directory.CreateDirectory(_backupDir);
        var snapshots = new List<PartitionSnapshot>();

        foreach (var file in Directory.EnumerateFiles(_backupDir, "*.json").OrderByDescending(File.GetCreationTimeUtc))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var snapshot = JsonSerializer.Deserialize<PartitionSnapshot>(json, SnapshotJsonOptions);
                if (snapshot is null)
                {
                    QuarantineCorruptFile(file, "deserialized to null");
                    continue;
                }

                snapshot.FilePath = file;
                snapshots.Add(snapshot);
            }
            catch (JsonException jex)
            {
                _log.Log($"Corrupt snapshot quarantined {Path.GetFileName(file)}: {jex.Message}");
                QuarantineCorruptFile(file, jex.Message);
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

    private void QuarantineCorruptFile(string path, string reason)
    {
        try
        {
            var corruptPath = path + ".corrupt";
            if (File.Exists(corruptPath)) File.Delete(corruptPath);
            File.Move(path, corruptPath);
            _log.Log($"Quarantined corrupt file: {Path.GetFileName(path)} — {reason}");
        }
        catch { }
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

    public async Task<string> BuildRecoveryPlanAsync(PartitionSnapshot snapshot)
    {
        var lines = new List<string>
        {
            "# PartitionPilot Recovery Plan",
            $"# Snapshot: {snapshot.FileName}",
            $"# Captured: {snapshot.CapturedAtText}",
            $"# Target Disk: {snapshot.DiskSummary}",
            ""
        };

        List<DiskInfo> currentDisks;
        try { currentDisks = await _wmiService.GetDisksAsync(); }
        catch { currentDisks = new List<DiskInfo>(); }

        var currentDisk = currentDisks.FirstOrDefault(d => d.Number == snapshot.DiskNumber);
        var mismatches = new List<string>();

        if (!snapshot.EffectiveDiskIdentity.Matches(currentDisk, out var identityMismatch))
            mismatches.Add(identityMismatch);

        if (currentDisk is not null &&
            !string.IsNullOrEmpty(snapshot.PartitionStyle) &&
            !snapshot.PartitionStyle.Equals(currentDisk.PartitionStyle, StringComparison.OrdinalIgnoreCase))
            mismatches.Add($"Partition style mismatch: snapshot={snapshot.PartitionStyle}, current={currentDisk.PartitionStyle}");

        if (mismatches.Count > 0)
        {
            lines.Add("## MISMATCHES DETECTED");
            lines.Add("# The current disk does not match the snapshot. Review carefully before proceeding.");
            foreach (var m in mismatches)
                lines.Add($"# WARNING: {m}");
            lines.Add("");
        }
        else
        {
            lines.Add("# Disk identity verified: stable identity, size, and partition style match the snapshot.");
            lines.Add("");
        }

        lines.Add("## Step 1: Diagnostic commands (safe, read-only)");
        lines.Add($"Get-Disk -Number {snapshot.DiskNumber}");
        lines.Add($"Get-Partition -DiskNumber {snapshot.DiskNumber} | Sort-Object PartitionNumber | Format-Table -AutoSize");
        lines.Add("Get-Volume | Sort-Object DriveLetter | Format-Table -AutoSize");
        lines.Add("");

        lines.Add("## Step 2: Captured partition layout (reference only)");
        foreach (var partition in snapshot.Partitions.OrderBy(p => p.PartitionNumber))
        {
            lines.Add(
                $"# Partition {partition.PartitionNumber}: {partition.LetterDisplay} {partition.Type}, {partition.SizeText}, offset {partition.OffsetText}, fs={partition.FileSystem}, roles={partition.RoleText}");
        }

        lines.Add("");
        lines.Add("# PartitionPilot does not generate destructive restore commands.");
        lines.Add("# Use Windows recovery tools or a specialist partition editor for restore operations.");
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
            $"# {snapshot.EffectiveDiskIdentity.StableIdentityText}",
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
