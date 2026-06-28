using System.Text.Json;
using PartitionPilot;
using PartitionPilot.Cli;

var log = new ConsoleLog();
var runner = new ProcessRunner();
var wmi = new WmiDiskService(runner, log);

if (args.Length == 0)
{
    PrintUsage();
    return 0;
}

var command = args[0].ToLowerInvariant();
var json = args.Contains("--json", StringComparer.OrdinalIgnoreCase);

try
{
    return command switch
    {
        "disks" => await ListDisksAsync(),
        "partitions" => await ListPartitionsAsync(),
        "volumes" => await ListVolumesAsync(),
        "smart" => await ShowSmartAsync(),
        "smart-history" => await ShowSmartHistoryAsync(),
        "smart-trends" => await ShowSmartTrendsAsync(),
        "health" => await ShowHealthAsync(),
        "alignment" => await ShowAlignmentAsync(),
        "snapshot" => await CaptureSnapshotAsync(),
        "diagnostics" or "diag" => await RunDiagnosticsAsync(),
        "plan" => await PlanOperationAsync(),
        "recovery-scan" => await RecoveryScanAsync(),
        "benchmark" => await RunBenchmarkAsync(),
        "temperature" or "temp" => await ShowTemperatureAsync(),
        "apply-layout" => await ApplyLayoutAsync(),
        "version" => ShowVersion(),
        "help" or "--help" or "-h" => PrintUsage(),
        _ => PrintUnknown(command)
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

int PrintUsage()
{
    Console.WriteLine($"PartitionPilot CLI v{UpdateService.GetCurrentVersion()}");
    Console.WriteLine();
    Console.WriteLine("Usage: pp <command> [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  disks                     List physical disks");
    Console.WriteLine("  partitions [--disk N]     List partitions (all disks or specific disk)");
    Console.WriteLine("  volumes                   List volumes with drive letters");
    Console.WriteLine("  smart --disk N            Show SMART data for a physical disk");
    Console.WriteLine("  smart-history --disk N    Show SMART history readings");
    Console.WriteLine("  smart-trends --disk N     Show SMART trend analysis");
    Console.WriteLine("  health                    Show health status for all physical disks");
    Console.WriteLine("  alignment                 Check 4K alignment for all partitions");
    Console.WriteLine("  temperature               Show current disk temperatures");
    Console.WriteLine("  benchmark --drive C       Run DiskSpd benchmark on a drive");
    Console.WriteLine("  snapshot --disk N         Capture partition layout snapshot to JSON");
    Console.WriteLine("  diagnostics               Check environment prerequisites");
    Console.WriteLine("  plan <op> [args]          Preview a partition operation (add --apply to execute)");
    Console.WriteLine("  recovery-scan --disk N    Scan for lost partition signatures (read-only)");
    Console.WriteLine("  apply-layout --file F --disk N  Apply a partition layout spec (add --apply, --replace to recreate)");
    Console.WriteLine("  version                   Show version");
    Console.WriteLine();
    Console.WriteLine("Plan operations:");
    Console.WriteLine("  plan delete --disk N --partition P");
    Console.WriteLine("  plan format --disk N --partition P --fs NTFS [--label Name]");
    Console.WriteLine("  plan change-letter --disk N --partition P --letter X");
    Console.WriteLine("  plan create --disk N --size 50GB [--fs NTFS] [--label Name]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --json                    Output as JSON");
    Console.WriteLine("  --disk N                  Target disk number");
    return 0;
}

int PrintUnknown(string cmd)
{
    Console.Error.WriteLine($"Unknown command: {cmd}");
    Console.Error.WriteLine("Run 'pp help' for usage.");
    return 1;
}

int? ParseDiskArg()
{
    var idx = Array.FindIndex(args, a => a.Equals("--disk", StringComparison.OrdinalIgnoreCase));
    if (idx < 0 || idx + 1 >= args.Length) return null;
    return int.TryParse(args[idx + 1], out var n) ? n : null;
}

static object DiskJson(DiskInfo d) => new
{
    d.Number,
    d.FriendlyName,
    d.Size,
    SizeText = SizeUtil.Format(d.Size),
    d.PartitionStyle,
    d.NumberOfPartitions,
    d.StoragePoolName,
    d.UniqueId,
    d.SerialNumber,
    d.Path,
    d.BusType,
    d.Location,
    DiskIdentity = d.ToIdentitySnapshot()
};

static void PrintTargetIdentity(DiskInfo disk)
{
    Console.WriteLine($"Target: {disk.DisplayText}");
    Console.WriteLine($"        {disk.IdentitySummary}");
}

static bool ValidateExpectedDiskIdentity(PartitionLayoutSpec spec, DiskInfo disk)
{
    if (spec.TargetDisk is null)
        return true;

    if (spec.TargetDisk.Matches(disk, out var mismatch))
        return true;

    Console.Error.WriteLine($"Target disk identity mismatch: {mismatch}");
    return false;
}

async Task<int> ListDisksAsync()
{
    var disks = await wmi.GetDisksAsync();
    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(disks.Select(DiskJson), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    Console.WriteLine($"{"Disk",-6} {"Name",-36} {"Size",-12} {"Style",-6} {"Parts",-6} Pool");
    Console.WriteLine(new string('-', 80));
    foreach (var d in disks)
    {
        Console.WriteLine($"{d.Number,-6} {Trunc(d.FriendlyName, 36),-36} {SizeUtil.Format(d.Size),-12} {d.PartitionStyle,-6} {d.NumberOfPartitions,-6} {d.StoragePoolName}");
        Console.WriteLine($"       {d.IdentitySummary}");
    }
    return 0;
}

async Task<int> ListPartitionsAsync()
{
    var diskNum = ParseDiskArg();
    var disks = await wmi.GetDisksAsync();
    var targets = diskNum.HasValue ? disks.Where(d => d.Number == diskNum.Value).ToList() : disks;

    if (json)
    {
        var result = new List<object>();
        foreach (var disk in targets)
        {
            var parts = await wmi.GetPartitionsAsync(disk.Number);
            result.Add(new { Disk = disk.Number, disk.FriendlyName, DiskIdentity = disk.ToIdentitySnapshot(), Partitions = parts.Select(p => new
            {
                p.PartitionNumber, DriveLetter = p.DriveLetter?.ToString(), p.Label,
                p.Size, SizeText = SizeUtil.Format(p.Size), p.FreeSpace, p.FileSystem, p.Type, p.Details
            })});
        }
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    foreach (var disk in targets)
    {
        Console.WriteLine($"Disk {disk.Number}: {disk.FriendlyName} ({SizeUtil.Format(disk.Size)}, {disk.PartitionStyle})");
        Console.WriteLine($"  {disk.IdentitySummary}");
        var parts = await wmi.GetPartitionsAsync(disk.Number);
        if (parts.Count == 0)
        {
            Console.WriteLine("  (no partitions)");
        }
        else
        {
            Console.WriteLine($"  {"#",-4} {"Letter",-8} {"Label",-20} {"Size",-12} {"Free",-12} {"FS",-8} {"Type",-14} Details");
            Console.WriteLine($"  {new string('-', 92)}");
            foreach (var p in parts)
            {
                Console.WriteLine($"  {p.PartitionNumber,-4} {p.LetterDisplay,-8} {Trunc(p.Label, 20),-20} {p.SizeText,-12} {p.FreeText,-12} {p.FileSystemDisplay,-8} {Trunc(p.Type, 14),-14} {p.Details}");
            }
        }
        Console.WriteLine();
    }
    return 0;
}

async Task<int> ListVolumesAsync()
{
    var volumes = await wmi.GetVolumesAsync();
    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(volumes.Select(v => new
        {
            v.DriveLetter, Label = v.FileSystemLabel, FileSystem = v.FileSystemType,
            v.Size, SizeText = SizeUtil.Format(v.Size),
            FreeSpace = v.SizeRemaining, FreeText = SizeUtil.Format(v.SizeRemaining), v.DriveType
        }), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    Console.WriteLine($"{"Drive",-8} {"Label",-24} {"FS",-8} {"Size",-12} {"Free",-12} Type");
    Console.WriteLine(new string('-', 72));
    foreach (var v in volumes)
    {
        var letter = v.DriveLetter.HasValue ? $"{v.DriveLetter}:" : "-";
        Console.WriteLine($"{letter,-8} {Trunc(v.FileSystemLabel, 24),-24} {v.FileSystemType,-8} {SizeUtil.Format(v.Size),-12} {SizeUtil.Format(v.SizeRemaining),-12} {v.DriveType}");
    }
    return 0;
}

async Task<int> ShowSmartAsync()
{
    var diskNum = ParseDiskArg();
    var physicals = await wmi.GetPhysicalDisksAsync();

    var targets = diskNum.HasValue
        ? physicals.Where(p => p.DeviceId == diskNum.Value.ToString()).ToList()
        : physicals;

    if (targets.Count == 0)
    {
        Console.Error.WriteLine(diskNum.HasValue ? $"No physical disk found with ID {diskNum.Value}." : "No physical disks found.");
        return 1;
    }

    var results = new List<object>();
    foreach (var phys in targets)
    {
        var smart = await wmi.GetSmartDataAsync(phys.DeviceId);
        if (json)
        {
            results.Add(new
            {
                phys.DeviceId, phys.FriendlyName, phys.MediaType, phys.BusType,
                Health = smart?.Health.ToString() ?? "Unknown", smart?.HealthReason,
                smart?.Temperature, smart?.Wear, smart?.PowerOnHours,
                smart?.ReallocatedSectors, smart?.PendingSectors, smart?.PowerCycleCount,
                TotalWritten = smart?.TotalBytesWritten, TotalRead = smart?.TotalBytesRead,
                smart?.NvmeAvailableSpare, smart?.NvmeMediaErrors,
                smart?.NvmeUnsafeShutdowns, smart?.NvmeControllerBusyMinutes,
                smart?.NvmeErrorLogEntries, smart?.NvmeCriticalWarning,
                CriticalWarningFlags = smart?.CriticalWarningFlags,
                Attributes = smart?.AllAttributes.Select(a => new { a.Id, a.Name, a.Current, a.Worst, a.RawValue })
            });
            continue;
        }

        Console.WriteLine($"Disk {phys.DeviceId}: {phys.FriendlyName} ({phys.MediaType}, {phys.BusType})");
        if (smart == null)
        {
            Console.WriteLine("  SMART data not available.");
        }
        else
        {
            Console.WriteLine($"  Health:             {smart.Health} — {smart.HealthReason}");
            if (smart.Temperature.HasValue) Console.WriteLine($"  Temperature:        {smart.Temperature} C");
            if (smart.Wear.HasValue) Console.WriteLine($"  Wear:               {smart.Wear}%");
            if (smart.PowerOnHours.HasValue) Console.WriteLine($"  Power-On Hours:     {smart.PowerOnHours:N0}");
            if (smart.PowerCycleCount.HasValue) Console.WriteLine($"  Power Cycles:       {smart.PowerCycleCount:N0}");
            if (smart.ReallocatedSectors.HasValue) Console.WriteLine($"  Reallocated Sectors:{smart.ReallocatedSectors:N0}");
            if (smart.PendingSectors.HasValue) Console.WriteLine($"  Pending Sectors:    {smart.PendingSectors:N0}");
            if (smart.TotalBytesWritten.HasValue) Console.WriteLine($"  Total Written:      {SizeUtil.Format(smart.TotalBytesWritten.Value)}");
            if (smart.TotalBytesRead.HasValue) Console.WriteLine($"  Total Read:         {SizeUtil.Format(smart.TotalBytesRead.Value)}");
            if (smart.NvmeAvailableSpare.HasValue) Console.WriteLine($"  NVMe Spare:         {smart.NvmeAvailableSpare}%");
            if (smart.NvmeMediaErrors.HasValue) Console.WriteLine($"  NVMe Media Errors:  {smart.NvmeMediaErrors:N0}");
            if (smart.NvmeUnsafeShutdowns.HasValue) Console.WriteLine($"  Unsafe Shutdowns:   {smart.NvmeUnsafeShutdowns:N0}");
            if (smart.NvmeControllerBusyMinutes.HasValue) Console.WriteLine($"  Controller Busy:    {smart.NvmeControllerBusyMinutes:N0} min");
            if (smart.NvmeErrorLogEntries.HasValue) Console.WriteLine($"  Error Log Entries:  {smart.NvmeErrorLogEntries:N0}");
            if (smart.CriticalWarningFlags.Count > 0) Console.WriteLine($"  Critical Warning:   {string.Join(", ", smart.CriticalWarningFlags)}");

            if (smart.AllAttributes.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"  {"ID",-6} {"Attribute",-32} {"Current",-10} {"Worst",-10} Raw");
                Console.WriteLine($"  {new string('-', 74)}");
                foreach (var a in smart.AllAttributes)
                {
                    Console.WriteLine($"  {a.Id,-6} {Trunc(a.Name, 32),-32} {a.Current,-10} {a.Worst,-10} {a.RawDisplay}");
                }
            }
        }
        Console.WriteLine();
    }

    if (json) Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

async Task<int> ShowHealthAsync()
{
    var physicals = await wmi.GetPhysicalDisksAsync();
    if (json)
    {
        var results = new List<object>();
        foreach (var p in physicals)
        {
            var smart = await wmi.GetSmartDataAsync(p.DeviceId);
            results.Add(new
            {
                p.DeviceId, p.FriendlyName, p.MediaType, p.BusType, Size = SizeUtil.Format(p.Size),
                Health = smart?.Health.ToString() ?? "Unknown", Reason = smart?.HealthReason ?? "SMART unavailable"
            });
        }
        Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    Console.WriteLine($"{"Disk",-6} {"Name",-30} {"Type",-6} {"Bus",-6} {"Size",-12} {"Health",-10} Reason");
    Console.WriteLine(new string('-', 90));
    foreach (var p in physicals)
    {
        var smart = await wmi.GetSmartDataAsync(p.DeviceId);
        var health = smart?.Health.ToString() ?? "Unknown";
        var reason = smart?.HealthReason ?? "SMART unavailable";
        Console.WriteLine($"{p.DeviceId,-6} {Trunc(p.FriendlyName, 30),-30} {p.MediaType,-6} {p.BusType,-6} {SizeUtil.Format(p.Size),-12} {health,-10} {reason}");
    }
    return 0;
}

async Task<int> ShowAlignmentAsync()
{
    var alignments = await wmi.GetAlignmentAuditAsync();
    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(alignments, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    Console.WriteLine($"{"Disk",-6} {"Part",-6} {"Letter",-8} {"Offset",-16} {"Aligned",-10} Status");
    Console.WriteLine(new string('-', 64));
    foreach (var a in alignments)
    {
        Console.WriteLine($"{a.DiskNumber,-6} {a.PartitionNumber,-6} {a.LetterDisplay,-8} {a.Offset,-16} {a.AlignedDisplay,-10} {a.Status}");
    }
    return 0;
}

async Task<int> CaptureSnapshotAsync()
{
    var diskNum = ParseDiskArg();
    if (!diskNum.HasValue)
    {
        Console.Error.WriteLine("--disk N is required for snapshot command.");
        return 1;
    }

    var backup = new PartitionTableBackup(wmi, log);
    await backup.SaveSnapshotAsync(diskNum.Value);

    var disks = await wmi.GetDisksAsync();
    var disk = disks.FirstOrDefault(d => d.Number == diskNum.Value);
    var parts = await wmi.GetPartitionsAsync(diskNum.Value);
    var snapshot = new
    {
        Timestamp = DateTime.UtcNow.ToString("o"),
        DiskNumber = diskNum.Value,
        DiskName = disk?.FriendlyName ?? "Unknown",
        DiskSize = disk?.Size ?? 0,
        PartitionStyle = disk?.PartitionStyle ?? "Unknown",
        DiskIdentity = disk?.ToIdentitySnapshot(),
        Partitions = parts.Select(p => new
        {
            p.PartitionNumber, DriveLetter = p.DriveLetter?.ToString(), p.Label,
            p.Size, p.Offset, p.Type, p.FileSystem
        })
    };
    Console.WriteLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
    Console.Error.WriteLine($"Snapshot saved to: {PartitionTableBackup.BackupDirectory}");
    return 0;
}

async Task<int> PlanOperationAsync()
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: pp plan <operation> [args] [--apply]");
        return 1;
    }

    var op = args[1].ToLowerInvariant();
    var diskNum = ParseDiskArg();
    var partNum = ParseIntArg("--partition");
    var apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);

    var plan = new
    {
        Operation = op,
        DiskNumber = diskNum,
        PartitionNumber = partNum,
        FileSystem = ParseStringArg("--fs"),
        Label = ParseStringArg("--label"),
        Letter = ParseStringArg("--letter"),
        Size = ParseStringArg("--size"),
        Apply = apply
    };

    string description;
    string riskLevel;
    string diskpartScript;

    switch (op)
    {
        case "delete":
            if (!diskNum.HasValue || !partNum.HasValue) { Console.Error.WriteLine("--disk N and --partition P required for delete."); return 1; }
            description = $"Delete partition {partNum.Value} on disk {diskNum.Value}";
            riskLevel = "High";
            diskpartScript = $"select disk {diskNum.Value}\nselect partition {partNum.Value}\ndelete partition override";
            break;

        case "format":
            if (!diskNum.HasValue || !partNum.HasValue) { Console.Error.WriteLine("--disk N and --partition P required for format."); return 1; }
            var fs = ProcessRunner.ValidateFileSystem(plan.FileSystem ?? "NTFS");
            var label = ProcessRunner.SanitizeLabel(plan.Label ?? "");
            description = $"Format partition {partNum.Value} on disk {diskNum.Value} as {fs}" + (label.Length > 0 ? $" (label: {label})" : "");
            riskLevel = "High";
            diskpartScript = $"select disk {diskNum.Value}\nselect partition {partNum.Value}\nformat fs={fs}{(label.Length > 0 ? $" label=\"{label}\"" : "")} quick";
            break;

        case "change-letter":
            if (!diskNum.HasValue || !partNum.HasValue) { Console.Error.WriteLine("--disk N and --partition P required."); return 1; }
            var letter = plan.Letter?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(letter) || letter.Length != 1 || !char.IsLetter(letter[0])) { Console.Error.WriteLine("--letter X required (single letter A-Z)."); return 1; }
            description = $"Assign letter {letter}: to partition {partNum.Value} on disk {diskNum.Value}";
            riskLevel = "Normal";
            diskpartScript = $"select disk {diskNum.Value}\nselect partition {partNum.Value}\nassign letter={letter}";
            break;

        case "create":
            if (!diskNum.HasValue) { Console.Error.WriteLine("--disk N required for create."); return 1; }
            var sizeStr = plan.Size ?? "max";
            var sizeClause = sizeStr.Equals("max", StringComparison.OrdinalIgnoreCase) ? "" : $" size={ParseSizeMB(sizeStr)}";
            var createFs = plan.FileSystem ?? "NTFS";
            ProcessRunner.ValidateFileSystem(createFs);
            var createLabel = ProcessRunner.SanitizeLabel(plan.Label ?? "");
            description = $"Create{(sizeClause.Length > 0 ? sizeClause.Trim() + " MB" : " max-size")} {createFs} partition on disk {diskNum.Value}";
            riskLevel = "Normal";
            diskpartScript = $"select disk {diskNum.Value}\ncreate partition primary{sizeClause}\nformat fs={createFs}{(createLabel.Length > 0 ? $" label=\"{createLabel}\"" : "")} quick\nassign";
            break;

        default:
            Console.Error.WriteLine($"Unknown plan operation: {op}. Supported: delete, format, change-letter, create.");
            return 1;
    }

    DiskInfo? targetDisk = null;
    DiskIdentitySnapshot? targetIdentity = null;
    if (diskNum.HasValue)
    {
        var disks = await wmi.GetDisksAsync();
        targetDisk = disks.FirstOrDefault(d => d.Number == diskNum.Value);
        if (targetDisk is null)
        {
            Console.Error.WriteLine($"Disk {diskNum.Value} not found.");
            return 1;
        }
        targetIdentity = targetDisk.ToIdentitySnapshot();
    }

    var planOutput = new
    {
        plan.Operation,
        Description = description,
        RiskLevel = riskLevel,
        DiskPartScript = diskpartScript,
        TargetDisk = targetIdentity,
        WillApply = apply
    };

    if (json)
        Console.WriteLine(JsonSerializer.Serialize(planOutput, new JsonSerializerOptions { WriteIndented = true }));
    else
    {
        Console.WriteLine($"Plan: {description}");
        Console.WriteLine($"Risk: {riskLevel}");
        if (targetDisk is not null)
            PrintTargetIdentity(targetDisk);
        Console.WriteLine($"DiskPart script:");
        foreach (var line in diskpartScript.Split('\n'))
            Console.WriteLine($"  {line}");
    }

    if (!apply)
    {
        Console.Error.WriteLine("\nDry run — add --apply to execute.");
        return 0;
    }

    if (riskLevel == "High")
    {
        if (targetIdentity is not null)
            Console.Error.WriteLine($"\nTarget identity:\n{targetIdentity.ConfirmationSummary}");
        Console.Error.Write($"\nWARNING: This is a destructive operation. Type YES to confirm: ");
        var confirm = Console.ReadLine()?.Trim();
        if (!string.Equals(confirm, "YES", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Cancelled.");
            return 1;
        }
    }

    try
    {
        if (targetIdentity is not null)
            await targetIdentity.VerifyCurrentAsync(wmi);
        Console.Error.WriteLine($"Executing: {description}...");
        await runner.RunDiskpartAsync(diskpartScript, log);
        Console.Error.WriteLine("Operation completed successfully.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Operation failed: {ex.Message}");
        return 1;
    }
}

string? ParseStringArg(string name)
{
    var idx = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    return (idx >= 0 && idx + 1 < args.Length) ? args[idx + 1] : null;
}

int? ParseIntArg(string name)
{
    var val = ParseStringArg(name);
    return val is not null && int.TryParse(val, out var n) ? n : null;
}

long ParseSizeMB(string sizeStr)
{
    sizeStr = sizeStr.Trim().ToUpperInvariant();
    if (sizeStr.EndsWith("TB")) return (long)(double.Parse(sizeStr.Replace("TB", "")) * 1024 * 1024);
    if (sizeStr.EndsWith("GB")) return (long)(double.Parse(sizeStr.Replace("GB", "")) * 1024);
    if (sizeStr.EndsWith("MB")) return (long)double.Parse(sizeStr.Replace("MB", ""));
    return long.Parse(sizeStr);
}

async Task<int> RecoveryScanAsync()
{
    var diskNum = ParseDiskArg();
    if (!diskNum.HasValue)
    {
        Console.Error.WriteLine("--disk N is required for recovery-scan.");
        return 1;
    }

    var disks = await wmi.GetDisksAsync();
    var disk = disks.FirstOrDefault(d => d.Number == diskNum.Value);
    if (disk is null)
    {
        Console.Error.WriteLine($"Disk {diskNum.Value} not found.");
        return 1;
    }

    var progress = new Progress<double>(p =>
        Console.Error.Write($"\rScanning... {p:F1}%"));

    var result = await PartitionRecoveryScanner.ScanAsync(
        disk.Number, disk.Size, log, progress);

    Console.Error.WriteLine();

    if (json)
        Console.WriteLine(PartitionRecoveryScanner.FormatJson(result));
    else
        Console.Write(PartitionRecoveryScanner.FormatReport(result));

    return 0;
}

async Task<int> RunDiagnosticsAsync()
{
    var checks = await EnvironmentDiagnostics.RunAllAsync(runner, log);
    if (json)
    {
        Console.WriteLine(EnvironmentDiagnostics.FormatJson(checks));
    }
    else
    {
        Console.Write(EnvironmentDiagnostics.FormatReport(checks));
    }
    var errorCount = checks.Count(c => c.Status == "Error");
    return errorCount > 0 ? 1 : 0;
}

int ShowVersion()
{
    Console.WriteLine($"PartitionPilot CLI v{UpdateService.GetCurrentVersion()}");
    return 0;
}

async Task<int> RunBenchmarkAsync()
{
    var driveStr = ParseStringArg("--drive");
    if (string.IsNullOrEmpty(driveStr) || driveStr.Length != 1 || !char.IsLetter(driveStr[0]))
    {
        Console.Error.WriteLine("--drive C required (single drive letter).");
        return 1;
    }

    var letter = char.ToUpperInvariant(driveStr[0]);
    Console.Error.WriteLine($"Ensuring DiskSpd is available...");
    await DiskSpdService.EnsureAvailableAsync(log, CancellationToken.None);

    Console.Error.WriteLine($"Running benchmark on {letter}:...");
    var progress = new Progress<string>(s => Console.Error.WriteLine($"  {s}"));
    var result = await DiskSpdService.RunProfilesAsync(letter, log, progress, CancellationToken.None);

    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            result.DriveLetter, result.DriveModel, DriveCapacity = result.DriveCapacityBytes,
            result.Timestamp,
            result.SeqReadMBps, result.SeqWriteMBps,
            result.Rnd4kReadMBps, result.Rnd4kWriteMBps,
            result.Rnd4kReadIOPS, result.Rnd4kWriteIOPS
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
        Console.WriteLine($"Benchmark Results — {letter}: ({result.DriveModel})");
        Console.WriteLine(new string('-', 50));
        Console.WriteLine($"  SEQ1M     Read:  {result.SeqReadMBps,8:F1} MB/s   Write: {result.SeqWriteMBps,8:F1} MB/s");
        Console.WriteLine($"  RND4K     Read:  {result.Rnd4kReadMBps,8:F1} MB/s   Write: {result.Rnd4kWriteMBps,8:F1} MB/s");
        Console.WriteLine($"  RND4K     IOPS:  {result.Rnd4kReadIOPS,8:F0}         IOPS:  {result.Rnd4kWriteIOPS,8:F0}");
    }
    return 0;
}

async Task<int> ShowSmartHistoryAsync()
{
    var diskNum = ParseDiskArg();
    if (!diskNum.HasValue) { Console.Error.WriteLine("--disk N required."); return 1; }

    var history = new SmartHistoryService();
    var readings = await history.GetHistoryAsync(diskNum.Value.ToString());

    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(readings.Select(r => new
        {
            r.Timestamp, r.Temperature, r.Wear, r.ReallocatedSectors, r.PendingSectors,
            r.PowerOnHours, r.TotalBytesWritten,
            r.NvmeAvailableSpare, r.NvmeMediaErrors
        }), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    if (readings.Count == 0)
    {
        Console.WriteLine($"No SMART history for disk {diskNum.Value}.");
        return 0;
    }

    Console.WriteLine($"SMART History — Disk {diskNum.Value} ({readings.Count} readings)");
    Console.WriteLine($"{"Timestamp",-22} {"Temp",-6} {"Wear",-6} {"Realloc",-8} {"Pending",-8} {"POH",-8} Written");
    Console.WriteLine(new string('-', 76));
    foreach (var r in readings)
    {
        Console.WriteLine($"{r.Timestamp:yyyy-MM-dd HH:mm}  {(r.Temperature.HasValue ? $"{r.Temperature}C" : "-"),-6} " +
            $"{(r.Wear.HasValue ? $"{r.Wear}%" : "-"),-6} " +
            $"{r.ReallocatedSectors?.ToString("N0") ?? "-",-8} " +
            $"{r.PendingSectors?.ToString("N0") ?? "-",-8} " +
            $"{r.PowerOnHours?.ToString("N0") ?? "-",-8} " +
            $"{(r.TotalBytesWritten.HasValue ? SizeUtil.Format(r.TotalBytesWritten.Value) : "-")}");
    }
    return 0;
}

async Task<int> ShowSmartTrendsAsync()
{
    var diskNum = ParseDiskArg();
    if (!diskNum.HasValue) { Console.Error.WriteLine("--disk N required."); return 1; }

    var history = new SmartHistoryService();
    var readings = await history.GetHistoryAsync(diskNum.Value.ToString());
    var trends = SmartHistoryService.AnalyzeTrends(readings);

    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(trends.Select(t => new
        {
            t.Attribute, Direction = t.Direction.ToString(), Severity = t.Severity.ToString(), t.Message
        }), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    if (trends.Count == 0)
    {
        Console.WriteLine($"No trends detected for disk {diskNum.Value} (need multiple readings).");
        return 0;
    }

    Console.WriteLine($"SMART Trends — Disk {diskNum.Value}");
    Console.WriteLine($"{"Severity",-10} {"Attribute",-24} {"Direction",-12} Message");
    Console.WriteLine(new string('-', 76));
    foreach (var t in trends)
    {
        Console.WriteLine($"{t.Severity,-10} {t.Attribute,-24} {t.Direction,-12} {t.Message}");
    }
    return 0;
}

async Task<int> ShowTemperatureAsync()
{
    var physicals = await wmi.GetPhysicalDisksAsync();
    var results = new List<object>();

    foreach (var p in physicals)
    {
        var smart = await wmi.GetSmartDataAsync(p.DeviceId);
        var temp = smart?.Temperature;
        if (json)
        {
            results.Add(new { p.DeviceId, p.FriendlyName, p.MediaType, Temperature = temp });
            continue;
        }

        var tempText = temp.HasValue ? $"{temp}°C" : "N/A";
        var warn = temp switch
        {
            >= 65 => " [CRITICAL]",
            >= 55 => " [WARNING]",
            _ => ""
        };
        Console.WriteLine($"Disk {p.DeviceId}: {p.FriendlyName,-30} {tempText}{warn}");
    }

    if (json)
        Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

async Task<int> ApplyLayoutAsync()
{
    var filePath = ParseStringArg("--file");
    var diskNum = ParseDiskArg();
    var apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);
    var replace = args.Contains("--replace", StringComparer.OrdinalIgnoreCase);

    if (string.IsNullOrEmpty(filePath)) { Console.Error.WriteLine("--file <path> required."); return 1; }
    if (!diskNum.HasValue) { Console.Error.WriteLine("--disk N required."); return 1; }
    if (!File.Exists(filePath)) { Console.Error.WriteLine($"Layout file not found: {filePath}"); return 1; }

    var specJson = File.ReadAllText(filePath);
    var spec = JsonSerializer.Deserialize<PartitionLayoutSpec>(specJson,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (spec is null || spec.Partitions.Count == 0)
    {
        Console.Error.WriteLine("Layout spec is empty or invalid.");
        return 1;
    }

    try
    {
        LayoutDiffService.Validate(spec);
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine($"Invalid layout spec: {ex.Message}");
        return 1;
    }

    var disks = await wmi.GetDisksAsync();
    var disk = disks.FirstOrDefault(d => d.Number == diskNum.Value);
    if (disk is null) { Console.Error.WriteLine($"Disk {diskNum.Value} not found."); return 1; }
    if (!ValidateExpectedDiskIdentity(spec, disk)) return 1;
    var targetIdentity = disk.ToIdentitySnapshot();

    var currentPartitions = await wmi.GetPartitionsAsync(diskNum.Value);
    var diff = LayoutDiffService.ComputeDiff(spec, disk, currentPartitions, allowDestructiveReplace: replace);

    if (json)
        Console.WriteLine(JsonSerializer.Serialize(diff.Select(d => new
        {
            d.Action, d.Description, d.RiskLevel, d.DiskpartScript, TargetDisk = targetIdentity, WillApply = apply, WillReplace = replace
        }), new JsonSerializerOptions { WriteIndented = true }));
    else
    {
        PrintTargetIdentity(disk);
        Console.WriteLine();
        Console.Write(LayoutDiffService.FormatPlan(diff));
    }

    var blocked = diff.Where(d => d.RiskLevel == "Blocked").ToList();
    if (blocked.Count > 0)
    {
        Console.Error.WriteLine("\nPlan cannot be applied without --replace because the current disk layout differs from the spec.");
        return apply ? 1 : 0;
    }

    if (!apply)
    {
        Console.Error.WriteLine("\nDry run — add --apply to execute.");
        return 0;
    }

    var hasDestructive = diff.Any(d => d.RiskLevel is "Destructive" or "High");
    if (hasDestructive)
    {
        Console.Error.WriteLine($"\nTarget identity:\n{targetIdentity.ConfirmationSummary}");
        Console.Error.Write("\nWARNING: This plan includes destructive operations. Type YES to confirm: ");
        var confirm = Console.ReadLine()?.Trim();
        if (!string.Equals(confirm, "YES", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Cancelled.");
            return 1;
        }
    }

    foreach (var step in diff)
    {
        await targetIdentity.VerifyCurrentAsync(wmi);
        if (string.IsNullOrEmpty(step.DiskpartScript))
        {
            Console.Error.WriteLine($"Skipping: {step.Description} (no automated script)");
            continue;
        }
        Console.Error.WriteLine($"Executing: {step.Description}...");
        try
        {
            await runner.RunDiskpartAsync(step.DiskpartScript, log);
            Console.Error.WriteLine($"  Done.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Failed: {ex.Message}");
            return 1;
        }
    }

    Console.Error.WriteLine("Layout applied successfully.");
    return 0;
}

static string Trunc(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
