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
        "health" => await ShowHealthAsync(),
        "alignment" => await ShowAlignmentAsync(),
        "snapshot" => await CaptureSnapshotAsync(),
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
    Console.WriteLine("  health                    Show health status for all physical disks");
    Console.WriteLine("  alignment                 Check 4K alignment for all partitions");
    Console.WriteLine("  snapshot --disk N         Capture partition layout snapshot to JSON");
    Console.WriteLine("  version                   Show version");
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

async Task<int> ListDisksAsync()
{
    var disks = await wmi.GetDisksAsync();
    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(disks.Select(d => new
        {
            d.Number, d.FriendlyName, d.Size, SizeText = SizeUtil.Format(d.Size),
            d.PartitionStyle, d.NumberOfPartitions, d.StoragePoolName
        }), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    Console.WriteLine($"{"Disk",-6} {"Name",-36} {"Size",-12} {"Style",-6} {"Parts",-6} Pool");
    Console.WriteLine(new string('-', 80));
    foreach (var d in disks)
    {
        Console.WriteLine($"{d.Number,-6} {Trunc(d.FriendlyName, 36),-36} {SizeUtil.Format(d.Size),-12} {d.PartitionStyle,-6} {d.NumberOfPartitions,-6} {d.StoragePoolName}");
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
            result.Add(new { Disk = disk.Number, disk.FriendlyName, Partitions = parts.Select(p => new
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

int ShowVersion()
{
    Console.WriteLine($"PartitionPilot CLI v{UpdateService.GetCurrentVersion()}");
    return 0;
}

static string Trunc(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
