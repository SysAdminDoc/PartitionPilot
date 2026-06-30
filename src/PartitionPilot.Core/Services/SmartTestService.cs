namespace PartitionPilot;

public enum SmartTestType { Short, Extended }

public class SmartTestResult
{
    public bool Started { get; set; }
    public string Message { get; set; } = "";
    public string? EstimatedDuration { get; set; }
}

public sealed class SmartctlInfo
{
    public bool IsAvailable { get; set; }
    public string Version { get; set; } = "";
    public string Path { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Remediation { get; set; } = "";
}

public sealed class SmartctlDeviceSpec
{
    public bool IsSupported { get; set; }
    public int DiskNumber { get; set; }
    public string DevicePath { get; set; } = "";
    public string DeviceTypeArgument { get; set; } = "";
    public string Status { get; set; } = "Unknown";
    public string Detail { get; set; } = "";
    public string Remediation { get; set; } = "";

    public string DeviceModePrefix => string.IsNullOrWhiteSpace(DeviceTypeArgument) ? "" : $"{DeviceTypeArgument} ";
}

public sealed class SmartctlCapability
{
    public bool CanRunSelfTest { get; set; }
    public string Status { get; set; } = "Unknown";
    public string Version { get; set; } = "";
    public string Path { get; set; } = "";
    public string DevicePath { get; set; } = "";
    public string DeviceTypeArgument { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Remediation { get; set; } = "";
}

public static class SmartTestService
{
    private const string SmartctlInstallRemediation =
        "Install smartmontools, add smartctl.exe to PATH, then refresh disk health diagnostics.";

    public static async Task<SmartTestResult> StartTestAsync(
        int diskNumber, SmartTestType testType, IProcessRunner runner, IActivityLog log)
    {
        return await StartTestAsync(new PhysicalDiskInfo { DeviceId = diskNumber.ToString() }, testType, runner, log);
    }

    public static async Task<SmartTestResult> StartTestAsync(
        PhysicalDiskInfo disk, SmartTestType testType, IProcessRunner runner, IActivityLog log)
    {
        var capability = await GetSelfTestCapabilityAsync(disk, runner, log);
        if (!capability.CanRunSelfTest)
        {
            log.Log($"SMART self-test unavailable: {capability.Detail}");
            return new SmartTestResult
            {
                Started = false,
                Message = string.IsNullOrWhiteSpace(capability.Remediation)
                    ? capability.Detail
                    : $"{capability.Detail} {capability.Remediation}"
            };
        }

        var device = GetDeviceSpec(disk);
        var testFlag = testType == SmartTestType.Short ? "short" : "long";

        log.Log($"Starting SMART {testFlag} self-test on disk {device.DiskNumber} using {device.DeviceModePrefix}{device.DevicePath}...");

        try
        {
            var output = await runner.RunExeAsync("smartctl", $"{device.DeviceModePrefix}-t {testFlag} {device.DevicePath}", log);
            var started = output.Contains("Testing has begun", StringComparison.OrdinalIgnoreCase) ||
                          output.Contains("self-test has begun", StringComparison.OrdinalIgnoreCase);

            string? duration = null;
            foreach (var line in output.Split('\n'))
            {
                if (line.Contains("complete after", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("estimated", StringComparison.OrdinalIgnoreCase))
                {
                    duration = line.Trim();
                    break;
                }
            }

            log.Log(started
                ? $"SMART {testFlag} self-test started on disk {device.DiskNumber}"
                : $"SMART self-test may not have started: {output.Trim()}");

            return new SmartTestResult
            {
                Started = started,
                Message = started ? $"SMART {testFlag} self-test started." : output.Trim(),
                EstimatedDuration = duration
            };
        }
        catch (Exception ex)
        {
            log.Log($"SMART self-test failed: {ex.Message}");
            return new SmartTestResult
            {
                Started = false,
                Message = $"smartctl not available or test failed: {ex.Message}"
            };
        }
    }

    public static async Task<string> GetTestStatusAsync(int diskNumber, IProcessRunner runner, IActivityLog log)
    {
        return await GetTestStatusAsync(new PhysicalDiskInfo { DeviceId = diskNumber.ToString() }, runner, log);
    }

    public static async Task<string> GetTestStatusAsync(PhysicalDiskInfo disk, IProcessRunner runner, IActivityLog log)
    {
        var device = GetDeviceSpec(disk);
        if (!device.IsSupported)
            return device.Detail;

        try
        {
            var output = await runner.RunExeAsync("smartctl", $"{device.DeviceModePrefix}-l selftest {device.DevicePath}", log,
                ignoreStderrOnSuccess: true);
            return output;
        }
        catch (Exception ex)
        {
            return $"Could not read self-test log: {ex.Message}";
        }
    }

    public static async Task<bool> IsSmartctlAvailableAsync(IProcessRunner runner, IActivityLog log)
    {
        var info = await GetSmartctlInfoAsync(runner, log);
        return info.IsAvailable;
    }

    public static async Task<SmartctlInfo> GetSmartctlInfoAsync(IProcessRunner runner, IActivityLog log)
    {
        try
        {
            var versionOutput = await runner.RunExeAsync("smartctl", "--version", log, ignoreStderrOnSuccess: true);
            var versionLine = versionOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim() ?? "smartctl available";
            var path = await TryResolveSmartctlPathAsync(runner, log);

            return new SmartctlInfo
            {
                IsAvailable = true,
                Version = ParseSmartctlVersion(versionLine),
                Path = path,
                Detail = string.IsNullOrWhiteSpace(path)
                    ? $"{versionLine}; smartctl is available on PATH"
                    : $"{versionLine}; path: {path}"
            };
        }
        catch (Exception ex)
        {
            return new SmartctlInfo
            {
                IsAvailable = false,
                Detail = $"smartctl not available: {ex.Message}",
                Remediation = SmartctlInstallRemediation
            };
        }
    }

    public static async Task<SmartctlCapability> GetSelfTestCapabilityAsync(
        PhysicalDiskInfo? disk, IProcessRunner runner, IActivityLog log)
    {
        var info = await GetSmartctlInfoAsync(runner, log);
        if (!info.IsAvailable)
        {
            return new SmartctlCapability
            {
                CanRunSelfTest = false,
                Status = "Missing",
                Detail = info.Detail,
                Remediation = info.Remediation
            };
        }

        if (disk is null)
        {
            return new SmartctlCapability
            {
                CanRunSelfTest = false,
                Status = "NoDisk",
                Version = info.Version,
                Path = info.Path,
                Detail = "Select a physical disk before running SMART self-tests."
            };
        }

        var device = GetDeviceSpec(disk);
        return new SmartctlCapability
        {
            CanRunSelfTest = device.IsSupported,
            Status = device.Status,
            Version = info.Version,
            Path = info.Path,
            DevicePath = device.DevicePath,
            DeviceTypeArgument = device.DeviceTypeArgument,
            Detail = device.IsSupported
                ? $"{info.Detail}; using {device.DeviceModePrefix}{device.DevicePath}. {device.Detail}".Trim()
                : device.Detail,
            Remediation = device.Remediation
        };
    }

    public static SmartctlDeviceSpec GetDeviceSpec(PhysicalDiskInfo disk)
    {
        if (!int.TryParse(disk.DeviceId, out var diskNumber) || diskNumber < 0)
        {
            return new SmartctlDeviceSpec
            {
                IsSupported = false,
                Status = "UnsupportedDevice",
                Detail = $"Cannot map disk DeviceId '{disk.DeviceId}' to a smartctl physical disk path.",
                Remediation = "Refresh disk inventory and retry with a physical disk number."
            };
        }

        var busType = disk.BusType.Trim();
        var devicePath = $"/dev/pd{diskNumber}";
        if (busType.Equals("NVMe", StringComparison.OrdinalIgnoreCase))
        {
            return new SmartctlDeviceSpec
            {
                IsSupported = true,
                DiskNumber = diskNumber,
                DevicePath = devicePath,
                DeviceTypeArgument = "-d nvme",
                Status = "OK",
                Detail = "NVMe device mode selected."
            };
        }

        if (busType.Equals("USB", StringComparison.OrdinalIgnoreCase))
        {
            return new SmartctlDeviceSpec
            {
                IsSupported = true,
                DiskNumber = diskNumber,
                DevicePath = devicePath,
                DeviceTypeArgument = "-d sat",
                Status = "Warning",
                Detail = "USB bridge detected; using SAT mode. Some bridges require a vendor-specific smartctl -d mode.",
                Remediation = "If the test fails, run diagnostics and use smartctl directly to identify the bridge-specific device mode."
            };
        }

        if (busType.Contains("RAID", StringComparison.OrdinalIgnoreCase) ||
            busType.Contains("Storage", StringComparison.OrdinalIgnoreCase))
        {
            return new SmartctlDeviceSpec
            {
                IsSupported = false,
                DiskNumber = diskNumber,
                DevicePath = devicePath,
                Status = "UnsupportedDevice",
                Detail = $"{busType} disks may require controller-specific smartctl device modes that PartitionPilot cannot infer safely.",
                Remediation = "Use the controller vendor's diagnostics or run smartctl manually with the correct controller mode."
            };
        }

        return new SmartctlDeviceSpec
        {
            IsSupported = true,
            DiskNumber = diskNumber,
            DevicePath = devicePath,
            Status = "OK",
            Detail = string.IsNullOrWhiteSpace(busType)
                ? "Default Windows physical disk path selected."
                : $"{busType} physical disk path selected."
        };
    }

    private static async Task<string> TryResolveSmartctlPathAsync(IProcessRunner runner, IActivityLog log)
    {
        try
        {
            var output = await runner.RunExeAsync("where.exe", "smartctl", log, ignoreStderrOnSuccess: true);
            return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string ParseSmartctlVersion(string versionLine)
    {
        var parts = versionLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0].Equals("smartctl", StringComparison.OrdinalIgnoreCase)
            ? parts[1]
            : versionLine;
    }
}
