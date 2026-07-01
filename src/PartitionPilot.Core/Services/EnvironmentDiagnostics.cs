using System.IO;
using System.Management;
using System.Security.Principal;
using System.Text.Json;

namespace PartitionPilot;

public class DiagnosticCheck
{
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "Unknown";
    public string Detail { get; set; } = "";
    public string Remediation { get; set; } = "";

    public bool IsOk => Status == "OK";
}

public static class EnvironmentDiagnostics
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static async Task<List<DiagnosticCheck>> RunAllAsync(
        IProcessRunner runner,
        IActivityLog log,
        bool includeRescueChecks = false)
    {
        var checks = new List<DiagnosticCheck>();

        checks.Add(CheckElevation());
        checks.Add(CheckDotNetVersion());
        checks.Add(CheckWindowsVersion());
        checks.AddRange(CheckWmiNamespaces());
        checks.AddRange(await CheckNativeToolsAsync(runner, log));
        checks.Add(CheckDiskSpdCache());
        checks.Add(CheckDataDirectory());
        if (includeRescueChecks)
            checks.AddRange(await RunRescueAsync(runner, log));

        return checks;
    }

    public static async Task<List<DiagnosticCheck>> RunRescueAsync(IProcessRunner runner, IActivityLog log)
    {
        var checks = new List<DiagnosticCheck>
        {
            CheckWinPeRuntime(),
            CheckWmiClass(@"\\.\root\Microsoft\Windows\Storage", "MSFT_Disk", "WinPE Storage API", "MSFT_Disk enumeration"),
            CheckWmiScope(@"\\.\root\CIMV2\Security\MicrosoftVolumeEncryption", "WinPE BitLocker WMI", "BitLocker provider")
        };

        checks.Add(await CheckNativeToolPathAsync(runner, log, "diskpart.exe", "WinPE DiskPart", "Partition scripting"));
        checks.Add(await CheckNativeToolPathAsync(runner, log, "dism.exe", "WinPE DISM", "WIM capture/apply"));
        checks.Add(await CheckNativeToolPathAsync(runner, log, "manage-bde.exe", "WinPE BitLocker Tool", "BitLocker unlock/status"));
        checks.Add(await CheckNativeToolPathAsync(runner, log, "bcdboot.exe", "WinPE BCDBoot", "Boot repair"));

        return checks;
    }

    public static bool IsWinPeRuntime(
        Func<string, string?>? getEnvironmentVariable = null,
        Func<bool>? miniNtRegistryExists = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        miniNtRegistryExists ??= MiniNtRegistryExists;

        var systemDrive = getEnvironmentVariable("SystemDrive") ?? "";
        return systemDrive.Equals("X:", StringComparison.OrdinalIgnoreCase) || miniNtRegistryExists();
    }

    public static DiagnosticCheck CheckWinPeRuntime(
        Func<string, string?>? getEnvironmentVariable = null,
        Func<bool>? miniNtRegistryExists = null)
    {
        var isWinPe = IsWinPeRuntime(getEnvironmentVariable, miniNtRegistryExists);
        return new DiagnosticCheck
        {
            Category = "WinPE Rescue",
            Name = "WinPE Runtime",
            Status = isWinPe ? "OK" : "Info",
            Detail = isWinPe
                ? "Running in Windows Preinstallation Environment"
                : "Not running in WinPE; rescue prerequisites are being checked against the current Windows environment",
            Remediation = isWinPe ? "" : "Boot the rescue media and run `pp diagnostics --rescue` again for final validation"
        };
    }

    public static string FormatReport(List<DiagnosticCheck> checks)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("PartitionPilot Environment Diagnostics");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine();

        string? lastCategory = null;
        foreach (var check in checks)
        {
            if (check.Category != lastCategory)
            {
                if (lastCategory is not null) sb.AppendLine();
                sb.AppendLine($"[{check.Category}]");
                lastCategory = check.Category;
            }

            var icon = check.IsOk ? "OK" : "!!";
            sb.AppendLine($"  [{icon}] {check.Name}: {check.Detail}");
            if (!check.IsOk && !string.IsNullOrEmpty(check.Remediation))
                sb.AppendLine($"       Fix: {check.Remediation}");
        }

        return sb.ToString();
    }

    public static string FormatJson(List<DiagnosticCheck> checks) =>
        JsonSerializer.Serialize(checks, JsonOpts);

    private static DiagnosticCheck CheckElevation()
    {
        bool isAdmin;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { isAdmin = false; }

        return new DiagnosticCheck
        {
            Category = "System",
            Name = "Administrator Privileges",
            Status = isAdmin ? "OK" : "Warning",
            Detail = isAdmin ? "Running elevated — disk operations available" : "Not elevated — disk operations will fail",
            Remediation = isAdmin ? "" : "Re-launch PartitionPilot as Administrator or use Elevate button"
        };
    }

    private static DiagnosticCheck CheckDotNetVersion()
    {
        return new DiagnosticCheck
        {
            Category = "System",
            Name = ".NET Runtime",
            Status = "OK",
            Detail = $".NET {Environment.Version} on {Environment.OSVersion.VersionString}"
        };
    }

    private static DiagnosticCheck CheckWindowsVersion()
    {
        var build = Environment.OSVersion.Version.Build;
        return new DiagnosticCheck
        {
            Category = "System",
            Name = "Windows Build",
            Status = build >= 19041 ? "OK" : "Warning",
            Detail = $"Build {build}",
            Remediation = build < 19041 ? "Some features require Windows 10 2004+ or Windows 11" : ""
        };
    }

    private static List<DiagnosticCheck> CheckWmiNamespaces()
    {
        var checks = new List<DiagnosticCheck>();

        checks.Add(CheckWmiScope(@"\\.\root\Microsoft\Windows\Storage", "WMI Storage", "Storage Management API (disks, partitions, volumes)"));
        checks.Add(CheckWmiScope(@"\\.\root\CIMV2", "WMI CIM", "CIM provider (reliability counters, system info)"));
        checks.Add(CheckWmiScope(@"\\.\root\CIMV2\Security\MicrosoftVolumeEncryption", "WMI BitLocker", "BitLocker encryption status"));

        return checks;
    }

    private static DiagnosticCheck CheckWmiScope(string scopePath, string name, string description)
    {
        try
        {
            var scope = new ManagementScope(scopePath);
            scope.Connect();
            return new DiagnosticCheck
            {
                Category = "WMI Providers",
                Name = name,
                Status = "OK",
                Detail = $"{description} — connected"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck
            {
                Category = "WMI Providers",
                Name = name,
                Status = "Error",
                Detail = $"{description} — {ex.Message}",
                Remediation = "Ensure the WMI service is running and the provider is registered"
            };
        }
    }

    private static DiagnosticCheck CheckWmiClass(string scopePath, string className, string name, string description)
    {
        try
        {
            var scope = new ManagementScope(scopePath);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM {className}"));
            searcher.Options.Timeout = TimeSpan.FromSeconds(5);
            using var results = searcher.Get();
            var count = results.Cast<ManagementBaseObject>().Count();

            return new DiagnosticCheck
            {
                Category = "WinPE Rescue",
                Name = name,
                Status = "OK",
                Detail = $"{description} - returned {count} object(s)"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck
            {
                Category = "WinPE Rescue",
                Name = name,
                Status = "Error",
                Detail = $"{description} - {ex.Message}",
                Remediation = "Add WinPE-WMI and WinPE-StorageWMI optional components, then rebuild the rescue media"
            };
        }
    }

    private static async Task<List<DiagnosticCheck>> CheckNativeToolsAsync(IProcessRunner runner, IActivityLog log)
    {
        var checks = new List<DiagnosticCheck>();

        checks.Add(await CheckToolAsync(runner, log, "diskpart", "/?", "DiskPart", "Partition management"));
        checks.Add(await CheckToolAsync(runner, log, "powershell.exe", "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"", "PowerShell", "Script execution"));
        checks.Add(await CheckToolAsync(runner, log, "dism.exe", "/?", "DISM", "Image capture/apply"));
        checks.Add(await CheckToolAsync(runner, log, "vssadmin", "list providers", "VSS", "Volume Shadow Copy"));
        checks.Add(await CheckVssWriterHealthAsync(runner, log));
        checks.Add(await CheckToolAsync(runner, log, "chkdsk.exe", "/?", "chkdsk", "Filesystem repair"));
        checks.Add(await CheckSmartctlAsync(runner, log));

        return checks;
    }

    public static async Task<DiagnosticCheck> CheckSmartctlAsync(IProcessRunner runner, IActivityLog log)
    {
        var info = await SmartTestService.GetSmartctlInfoAsync(runner, log);
        return new DiagnosticCheck
        {
            Category = "Native Tools",
            Name = "smartctl",
            Status = info.IsAvailable ? "OK" : "Error",
            Detail = info.IsAvailable
                ? $"SMART self-tests available - version {info.Version}, path: {(string.IsNullOrWhiteSpace(info.Path) ? "PATH lookup unavailable" : info.Path)}"
                : info.Detail,
            Remediation = info.IsAvailable ? "" : info.Remediation
        };
    }

    private static async Task<DiagnosticCheck> CheckVssWriterHealthAsync(IProcessRunner runner, IActivityLog log)
    {
        try
        {
            var report = await VssSnapshotService.CheckWriterHealthAsync(runner, log);
            return new DiagnosticCheck
            {
                Category = "Native Tools",
                Name = "VSS Writers",
                Status = report.IsHealthy ? "OK" : "Error",
                Detail = report.Summary,
                Remediation = report.IsHealthy
                    ? ""
                    : "Run `vssadmin list writers` in an elevated terminal, resolve failed writers, then retry image capture"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck
            {
                Category = "Native Tools",
                Name = "VSS Writers",
                Status = "Error",
                Detail = $"Could not list VSS writers: {ex.Message}",
                Remediation = "Ensure the Volume Shadow Copy service is available and run diagnostics from an elevated session"
            };
        }
    }

    private static async Task<DiagnosticCheck> CheckToolAsync(IProcessRunner runner, IActivityLog log,
        string tool, string args, string name, string description)
    {
        try
        {
            await runner.RunExeAsync(tool, args, log, ignoreStderrOnSuccess: true);
            return new DiagnosticCheck
            {
                Category = "Native Tools",
                Name = name,
                Status = "OK",
                Detail = $"{description} — available"
            };
        }
        catch
        {
            return new DiagnosticCheck
            {
                Category = "Native Tools",
                Name = name,
                Status = "Error",
                Detail = $"{description} — not found or failed",
                Remediation = $"Ensure {tool} is on the system PATH"
            };
        }
    }

    private static async Task<DiagnosticCheck> CheckNativeToolPathAsync(
        IProcessRunner runner,
        IActivityLog log,
        string tool,
        string name,
        string description)
    {
        var system32Path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            tool);
        if (File.Exists(system32Path))
        {
            return new DiagnosticCheck
            {
                Category = "WinPE Rescue",
                Name = name,
                Status = "OK",
                Detail = $"{description} - available at {system32Path}"
            };
        }

        try
        {
            var output = await runner.RunExeAsync("where.exe", tool, log, ignoreStderrOnSuccess: true);
            var path = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? tool;
            return new DiagnosticCheck
            {
                Category = "WinPE Rescue",
                Name = name,
                Status = "OK",
                Detail = $"{description} - available at {path}"
            };
        }
        catch
        {
            return new DiagnosticCheck
            {
                Category = "WinPE Rescue",
                Name = name,
                Status = "Error",
                Detail = $"{description} - {tool} not found",
                Remediation = $"Add the WinPE optional component that provides {tool}, or copy it into the rescue image PATH"
            };
        }
    }

    private static DiagnosticCheck CheckDiskSpdCache()
    {
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PartitionPilot", "tools");
        var diskSpdPath = Path.Combine(cacheDir, "diskspd.exe");

        if (File.Exists(diskSpdPath))
        {
            return new DiagnosticCheck
            {
                Category = "Tools Cache",
                Name = "DiskSpd",
                Status = "OK",
                Detail = $"Cached at {diskSpdPath}"
            };
        }

        return new DiagnosticCheck
        {
            Category = "Tools Cache",
            Name = "DiskSpd",
            Status = "Info",
            Detail = "Not cached — will download from GitHub on first benchmark",
            Remediation = "Run a benchmark to trigger DiskSpd download, or pre-place diskspd.exe in ProgramData/PartitionPilot/tools"
        };
    }

    private static DiagnosticCheck CheckDataDirectory()
    {
        var programData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PartitionPilot");
        try
        {
            Directory.CreateDirectory(programData);
            var testFile = Path.Combine(programData, ".diag_test");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return new DiagnosticCheck
            {
                Category = "Storage",
                Name = "Data Directory",
                Status = "OK",
                Detail = $"{programData} — writable"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck
            {
                Category = "Storage",
                Name = "Data Directory",
                Status = "Error",
                Detail = $"{programData} — {ex.Message}",
                Remediation = "Ensure the application has write access to the ProgramData directory"
            };
        }
    }

    private static bool MiniNtRegistryExists()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\MiniNT");
            return key is not null;
        }
        catch
        {
            return false;
        }
    }
}
