using System.Management;
using System.Text.Json;

namespace PartitionPilot;

public class WmiDiskService : IWmiDiskService
{
    private const string StorageScope = @"\\.\root\Microsoft\Windows\Storage";
    private const string CimScope = @"\\.\root\CIMV2";

    private readonly ProcessRunner _runner;
    private readonly ActivityLog _log;

    public WmiDiskService(ProcessRunner runner, ActivityLog log)
    {
        _runner = runner;
        _log = log;
    }

    // ───────────────────────── Disks ─────────────────────────

    public Task<List<DiskInfo>> GetDisksAsync() => Task.Run(() =>
    {
        var list = new List<DiskInfo>();
        try
        {
            var scope = new ManagementScope(StorageScope);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT * FROM MSFT_Disk WHERE OperationalStatus = 1"));

            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var style = Convert.ToInt32(obj["PartitionStyle"]);
                    list.Add(new DiskInfo
                    {
                        Number = Convert.ToInt32(obj["Number"]),
                        FriendlyName = obj["FriendlyName"]?.ToString() ?? "",
                        Size = Convert.ToInt64(obj["Size"]),
                        PartitionStyle = style switch { 1 => "MBR", 2 => "GPT", _ => "RAW" },
                        LargestFreeExtent = Convert.ToInt64(obj["LargestFreeExtent"] ?? 0L),
                        NumberOfPartitions = Convert.ToInt32(obj["NumberOfPartitions"] ?? 0)
                    });
                }
            }
        }
        catch (Exception ex) { _log.Log($"WMI GetDisks failed: {ex.Message}"); }
        return list;
    });

    // ───────────────────── Partitions ─────────────────────

    public Task<List<PartitionInfo>> GetPartitionsAsync(int diskNumber) => Task.Run(() =>
    {
        var list = new List<PartitionInfo>();
        try
        {
            var scope = new ManagementScope(StorageScope);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery($"SELECT * FROM MSFT_Partition WHERE DiskNumber = {diskNumber}"));

            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var rawLetter = Convert.ToChar(obj["DriveLetter"] ?? '\0');
                    char? letter = rawLetter == '\0' ? null : rawLetter;

                    var gptType = obj["GptType"]?.ToString() ?? "";
                    var type = MapGptType(gptType);

                    list.Add(new PartitionInfo
                    {
                        PartitionNumber = Convert.ToInt32(obj["PartitionNumber"]),
                        DriveLetter = letter,
                        Size = Convert.ToInt64(obj["Size"]),
                        Offset = Convert.ToInt64(obj["Offset"]),
                        IsBoot = Convert.ToBoolean(obj["IsBoot"] ?? false),
                        IsSystem = Convert.ToBoolean(obj["IsSystem"] ?? false),
                        IsActive = Convert.ToBoolean(obj["IsActive"] ?? false),
                        IsHidden = Convert.ToBoolean(obj["IsHidden"] ?? false),
                        DiskNumber = diskNumber,
                        Type = type
                    });
                }
            }
        }
        catch (Exception ex) { _log.Log($"WMI GetPartitions failed: {ex.Message}"); }

        list.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        return list;
    });

    private static string MapGptType(string gptType)
    {
        if (string.IsNullOrEmpty(gptType)) return "Unknown";
        var lower = gptType.ToLowerInvariant();
        if (lower.Contains("ebd0a0a2")) return "Basic";
        if (lower.Contains("c12a7328")) return "System";
        if (lower.Contains("de94bba4")) return "Recovery";
        if (lower.Contains("e3c9e316")) return "Reserved";
        return "Unknown";
    }

    // ──────────────────────── Volumes ────────────────────────

    public Task<List<VolumeInfo>> GetVolumesAsync() => Task.Run(() =>
    {
        var list = new List<VolumeInfo>();
        try
        {
            var scope = new ManagementScope(StorageScope);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT * FROM MSFT_Volume"));

            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var rawLetter = Convert.ToChar(obj["DriveLetter"] ?? '\0');
                    char? letter = rawLetter == '\0' ? null : rawLetter;

                    var fsTypeInt = Convert.ToUInt16(obj["FileSystemType"] ?? 0);
                    var fsType = fsTypeInt switch
                    {
                        2 => "NTFS",
                        3 => "FAT",
                        4 => "FAT32",
                        5 => "exFAT",
                        6 => "ReFS",
                        _ => "Unknown"
                    };

                    list.Add(new VolumeInfo
                    {
                        DriveLetter = letter,
                        FileSystemLabel = obj["FileSystemLabel"]?.ToString() ?? "",
                        FileSystemType = fsType,
                        Size = Convert.ToInt64(obj["Size"] ?? 0L),
                        SizeRemaining = Convert.ToInt64(obj["SizeRemaining"] ?? 0L),
                        DriveType = Convert.ToString(obj["DriveType"] ?? "") ?? ""
                    });
                }
            }
        }
        catch (Exception ex) { _log.Log($"WMI GetVolumes failed: {ex.Message}"); }
        return list;
    });

    // ──────────────── Enrich Partitions ──────────────────

    public static void EnrichPartitionsWithVolumes(List<PartitionInfo> partitions, List<VolumeInfo> volumes)
    {
        var volByLetter = new Dictionary<char, VolumeInfo>();
        foreach (var v in volumes)
        {
            if (v.DriveLetter.HasValue)
                volByLetter[v.DriveLetter.Value] = v;
        }

        foreach (var p in partitions)
        {
            if (p.DriveLetter.HasValue && volByLetter.TryGetValue(p.DriveLetter.Value, out var vol))
            {
                p.Label = vol.FileSystemLabel;
                p.FileSystem = vol.FileSystemType;
                p.FreeSpace = vol.SizeRemaining;
            }
        }
    }

    // ──────────────── Physical Disks ────────────────────

    public Task<List<PhysicalDiskInfo>> GetPhysicalDisksAsync() => Task.Run(() =>
    {
        var list = new List<PhysicalDiskInfo>();
        try
        {
            var scope = new ManagementScope(StorageScope);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT * FROM MSFT_PhysicalDisk"));

            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var mediaInt = Convert.ToInt32(obj["MediaType"] ?? 0);
                    var busInt = Convert.ToInt32(obj["BusType"] ?? 0);

                    list.Add(new PhysicalDiskInfo
                    {
                        DeviceId = obj["DeviceId"]?.ToString() ?? "",
                        FriendlyName = obj["FriendlyName"]?.ToString() ?? "",
                        SerialNumber = (obj["SerialNumber"]?.ToString() ?? "").Trim(),
                        FirmwareVersion = obj["FirmwareVersion"]?.ToString() ?? "",
                        Size = Convert.ToInt64(obj["Size"] ?? 0L),
                        LogicalSectorSize = Convert.ToInt32(obj["LogicalSectorSize"] ?? 0),
                        PhysicalSectorSize = Convert.ToInt32(obj["PhysicalSectorSize"] ?? 0),
                        HealthStatus = Convert.ToString(obj["HealthStatus"] ?? "") ?? "",
                        OperationalStatus = Convert.ToString(obj["OperationalStatus"] ?? "") ?? "",
                        MediaType = mediaInt switch { 3 => "HDD", 4 => "SSD", _ => "Unknown" },
                        BusType = MapBusType(busInt)
                    });
                }
            }
        }
        catch (Exception ex) { _log.Log($"WMI GetPhysicalDisks failed: {ex.Message}"); }
        return list;
    });

    private static string MapBusType(int busType) => busType switch
    {
        1 => "SCSI",
        3 => "ATA",
        5 => "USB",
        6 => "SAS",
        7 or 11 => "SATA",
        17 => "NVMe",
        _ => busType.ToString()
    };

    // ──────────────────── SMART Data ─────────────────────

    public async Task<SmartData?> GetSmartDataAsync(string deviceId)
    {
        // Attempt 1: direct WMI query
        try
        {
            return await Task.Run(() =>
            {
                var scope = new ManagementScope(StorageScope);
                scope.Connect();
                using var searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery($"SELECT * FROM MSFT_StorageReliabilityCounter WHERE DeviceId = '{deviceId}'"));

                foreach (ManagementObject obj in searcher.Get())
                {
                    using (obj)
                    {
                        return new SmartData
                        {
                            Temperature = GetNullableInt(obj, "Temperature"),
                            ReadErrorsTotal = GetNullableLong(obj, "ReadErrorsTotal"),
                            ReadErrorsCorrected = GetNullableLong(obj, "ReadErrorsCorrected"),
                            WriteErrorsTotal = GetNullableLong(obj, "WriteErrorsTotal"),
                            WriteErrorsCorrected = GetNullableLong(obj, "WriteErrorsCorrected"),
                            Wear = GetNullableInt(obj, "Wear"),
                            PowerOnHours = GetNullableInt(obj, "PowerOnHours"),
                            ReadLatencyMax = GetNullableLong(obj, "ReadLatencyMax"),
                            WriteLatencyMax = GetNullableLong(obj, "WriteLatencyMax")
                        };
                    }
                }
                return null;
            });
        }
        catch (Exception ex) { _log.Log($"WMI SMART query failed, trying PowerShell: {ex.Message}"); }

        // Attempt 2: PowerShell fallback
        try
        {
            var json = await _runner.RunPowerShellAsync(
                $"Get-StorageReliabilityCounter -PhysicalDisk (Get-PhysicalDisk -DeviceNumber {deviceId}) | ConvertTo-Json");

            if (string.IsNullOrWhiteSpace(json)) return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new SmartData
            {
                Temperature = GetJsonInt(root, "Temperature"),
                ReadErrorsTotal = GetJsonLong(root, "ReadErrorsTotal"),
                ReadErrorsCorrected = GetJsonLong(root, "ReadErrorsCorrected"),
                WriteErrorsTotal = GetJsonLong(root, "WriteErrorsTotal"),
                WriteErrorsCorrected = GetJsonLong(root, "WriteErrorsCorrected"),
                Wear = GetJsonInt(root, "Wear"),
                PowerOnHours = GetJsonInt(root, "PowerOnHours"),
                ReadLatencyMax = GetJsonLong(root, "ReadLatencyMax"),
                WriteLatencyMax = GetJsonLong(root, "WriteLatencyMax")
            };
        }
        catch (Exception ex) { _log.Log($"PowerShell SMART query failed: {ex.Message}"); return null; }
    }

    // ──────────────── Alignment Audit ───────────────────

    public Task<List<AlignmentInfo>> GetAlignmentAuditAsync() => Task.Run(() =>
    {
        var list = new List<AlignmentInfo>();
        try
        {
            var scope = new ManagementScope(StorageScope);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT * FROM MSFT_Partition"));

            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var offset = Convert.ToInt64(obj["Offset"]);
                    var rawLetter = Convert.ToChar(obj["DriveLetter"] ?? '\0');
                    var isAligned = offset % 4096 == 0;

                    list.Add(new AlignmentInfo
                    {
                        DiskNumber = Convert.ToInt32(obj["DiskNumber"]),
                        PartitionNumber = Convert.ToInt32(obj["PartitionNumber"]),
                        DriveLetter = rawLetter == '\0' ? null : rawLetter,
                        Offset = offset,
                        IsAligned = isAligned,
                        Status = isAligned ? "Aligned (4K)" : $"Misaligned (offset {offset} not 4K-aligned)"
                    });
                }
            }
        }
        catch (Exception ex) { _log.Log($"WMI AlignmentAudit failed: {ex.Message}"); }
        return list;
    });

    // ──────────────── Pagefile Locations ─────────────────

    public Task<HashSet<char>> GetPagefileLocationsAsync() => Task.Run(() =>
    {
        var set = new HashSet<char>();
        try
        {
            var scope = new ManagementScope(CimScope);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT Name FROM Win32_PageFileUsage"));

            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var name = obj["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name) && name.Length >= 1 && char.IsLetter(name[0]))
                        set.Add(char.ToUpperInvariant(name[0]));
                }
            }
        }
        catch (Exception ex) { _log.Log($"WMI PagefileLocations failed: {ex.Message}"); }
        return set;
    });

    // ──────────────── Available Letters ──────────────────

    public async Task<List<char>> GetAvailableLettersAsync()
    {
        var partitions = new List<char>();
        try
        {
            var allPartitions = await Task.Run(() =>
            {
                var used = new List<char>();
                var scope = new ManagementScope(StorageScope);
                scope.Connect();
                using var searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT DriveLetter FROM MSFT_Partition"));

                foreach (ManagementObject obj in searcher.Get())
                {
                    using (obj)
                    {
                        var rawLetter = Convert.ToChar(obj["DriveLetter"] ?? '\0');
                        if (rawLetter != '\0')
                            used.Add(char.ToUpperInvariant(rawLetter));
                    }
                }
                return used;
            });
            partitions = allPartitions;
        }
        catch (Exception ex) { _log.Log($"WMI GetAvailableLetters failed: {ex.Message}"); }

        var usedSet = new HashSet<char>(partitions);
        var available = new List<char>();
        for (var c = 'D'; c <= 'Z'; c++)
        {
            if (!usedSet.Contains(c))
                available.Add(c);
        }
        return available;
    }

    // ──────────── Partition Supported Size ───────────────

    public async Task<(long Min, long Max)> GetPartitionSupportedSizeAsync(char driveLetter)
    {
        var json = await _runner.RunPowerShellAsync(
            $"(Get-PartitionSupportedSize -DriveLetter '{driveLetter}') | ConvertTo-Json");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var min = root.GetProperty("SizeMin").GetInt64();
        var max = root.GetProperty("SizeMax").GetInt64();
        return (min, max);
    }

    // ──────────────── Mounted Images ────────────────────

    public Task<List<MountedImageInfo>> GetMountedImagesAsync() => Task.Run(() =>
    {
        var list = new List<MountedImageInfo>();
        try
        {
            var scope = new ManagementScope(StorageScope);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT * FROM MSFT_DiskImage WHERE Attached = True"));

            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var storageTypeInt = Convert.ToInt32(obj["StorageType"] ?? 0);
                    var info = new MountedImageInfo
                    {
                        ImagePath = obj["ImagePath"]?.ToString() ?? "",
                        StorageType = storageTypeInt switch
                        {
                            1 => "ISO",
                            2 => "VHD",
                            3 => "VHDX",
                            _ => "Unknown"
                        },
                        Size = Convert.ToInt64(obj["Size"] ?? 0L)
                    };

                    // Try to find the associated drive letter via volumes
                    try
                    {
                        info.DriveLetter = FindImageDriveLetter(scope, obj);
                    }
                    catch { /* leave null */ }

                    list.Add(info);
                }
            }
        }
        catch (Exception ex) { _log.Log($"WMI GetMountedImages failed: {ex.Message}"); }
        return list;
    });

    private static char? FindImageDriveLetter(ManagementScope scope, ManagementObject imageObj)
    {
        try
        {
            var devicePath = imageObj["DevicePath"]?.ToString();
            if (string.IsNullOrEmpty(devicePath)) return null;

            // MSFT_DiskImage.DevicePath maps to MSFT_Disk.Path — find the disk number
            using var diskSearcher = new ManagementObjectSearcher(scope,
                new ObjectQuery($"SELECT Number FROM MSFT_Disk WHERE Path = '{devicePath.Replace("'", "''")}'"));

            int? diskNumber = null;
            foreach (ManagementObject disk in diskSearcher.Get())
            {
                using (disk)
                    diskNumber = Convert.ToInt32(disk["Number"]);
            }

            if (diskNumber is null) return null;

            // Find the first partition with a drive letter on that disk
            using var partSearcher = new ManagementObjectSearcher(scope,
                new ObjectQuery($"SELECT DriveLetter FROM MSFT_Partition WHERE DiskNumber = {diskNumber}"));

            foreach (ManagementObject part in partSearcher.Get())
            {
                using (part)
                {
                    var rawLetter = Convert.ToChar(part["DriveLetter"] ?? '\0');
                    if (rawLetter != '\0')
                        return rawLetter;
                }
            }
        }
        catch { /* leave null */ }
        return null;
    }

    // ──────────────── WMI Helpers ────────────────────────

    private static int? GetNullableInt(ManagementObject obj, string property)
    {
        try { var v = obj[property]; return v != null ? Convert.ToInt32(v) : null; }
        catch { return null; }
    }

    private static long? GetNullableLong(ManagementObject obj, string property)
    {
        try { var v = obj[property]; return v != null ? Convert.ToInt64(v) : null; }
        catch { return null; }
    }

    private static int? GetJsonInt(JsonElement el, string property)
    {
        if (el.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.Number)
            return val.GetInt32();
        return null;
    }

    private static long? GetJsonLong(JsonElement el, string property)
    {
        if (el.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.Number)
            return val.GetInt64();
        return null;
    }
}
