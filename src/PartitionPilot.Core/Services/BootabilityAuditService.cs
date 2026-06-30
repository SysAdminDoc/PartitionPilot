using System.Text;

namespace PartitionPilot;

public enum BootabilityAuditStatus
{
    Pass,
    Warning,
    Fail
}

public sealed class BootabilityAuditIssue
{
    public BootabilityAuditStatus Severity { get; set; }
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string Remediation { get; set; } = "";
}

public sealed class BootabilityAuditReport
{
    public int DiskNumber { get; set; }
    public string PartitionStyle { get; set; } = "";
    public BootabilityAuditStatus Status { get; set; } = BootabilityAuditStatus.Warning;
    public bool WindowsDetected { get; set; }
    public char? WindowsDriveLetter { get; set; }
    public bool SystemPartitionFound { get; set; }
    public char? SystemPartitionDriveLetter { get; set; }
    public bool BcdStoreFound { get; set; }
    public string WinReStatus { get; set; } = "Not checked";
    public string SuggestedBootRepairPlan { get; set; } = "";
    public List<BootabilityAuditIssue> Issues { get; set; } = new();

    public string Summary =>
        $"Bootability audit {Status}: Disk {DiskNumber} {(string.IsNullOrWhiteSpace(PartitionStyle) ? "Unknown" : PartitionStyle)}; " +
        (WindowsDetected
            ? $"Windows at {WindowsDriveLetter}:; system partition {(SystemPartitionFound ? "found" : "missing")}; BCD {(BcdStoreFound ? "found" : "missing")}; WinRE {WinReStatus}."
            : "no Windows installation detected.");

    public string FormatReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Summary);
        foreach (var issue in Issues)
            sb.AppendLine($"- {issue.Severity}: {issue.Message}{(string.IsNullOrWhiteSpace(issue.Remediation) ? "" : $" Fix: {issue.Remediation}")}");
        if (!string.IsNullOrWhiteSpace(SuggestedBootRepairPlan))
        {
            sb.AppendLine("Suggested non-destructive boot repair plan:");
            sb.AppendLine(SuggestedBootRepairPlan);
        }
        return sb.ToString().TrimEnd();
    }
}

public static class BootabilityAuditService
{
    public static async Task<BootabilityAuditReport> AuditAsync(
        int diskNumber,
        IWmiDiskService wmi,
        IProcessRunner runner,
        IActivityLog log,
        char? knownWindowsDrive = null,
        CancellationToken ct = default)
    {
        var disks = await wmi.GetDisksAsync();
        var disk = disks.FirstOrDefault(d => d.Number == diskNumber);
        if (disk is null)
        {
            return new BootabilityAuditReport
            {
                DiskNumber = diskNumber,
                PartitionStyle = "Unknown",
                Status = BootabilityAuditStatus.Fail,
                SuggestedBootRepairPlan = "Refresh disk inventory and rerun the audit.",
                Issues =
                {
                    new BootabilityAuditIssue
                    {
                        Severity = BootabilityAuditStatus.Fail,
                        Code = "DiskMissing",
                        Message = $"Disk {diskNumber} is not present after the operation.",
                        Remediation = "Refresh disk inventory and verify the target disk identity."
                    }
                }
            };
        }

        var partitions = await wmi.GetPartitionsAsync(diskNumber);
        return await AuditAsync(disk, partitions, runner, log, knownWindowsDrive, ct);
    }

    public static async Task<BootabilityAuditReport> AuditAsync(
        DiskInfo disk,
        IReadOnlyList<PartitionInfo> partitions,
        IProcessRunner runner,
        IActivityLog log,
        char? knownWindowsDrive = null,
        CancellationToken ct = default)
    {
        var report = new BootabilityAuditReport
        {
            DiskNumber = disk.Number,
            PartitionStyle = string.IsNullOrWhiteSpace(disk.PartitionStyle) ? "Unknown" : disk.PartitionStyle
        };

        if (disk.IsRaw || partitions.Count == 0)
        {
            report.Issues.Add(new BootabilityAuditIssue
            {
                Severity = BootabilityAuditStatus.Fail,
                Code = "NoPartitions",
                Message = "The target disk has no readable partition layout after the operation.",
                Remediation = "Review the operation log and rerun restore/clone before attempting boot repair."
            });
            FinalizeReport(report);
            return report;
        }

        var windowsDrive = await FindWindowsDriveAsync(partitions, runner, log, knownWindowsDrive, ct);
        report.WindowsDriveLetter = windowsDrive;
        report.WindowsDetected = windowsDrive.HasValue;

        var isGpt = disk.PartitionStyle.Equals("GPT", StringComparison.OrdinalIgnoreCase);
        var isMbr = disk.PartitionStyle.Equals("MBR", StringComparison.OrdinalIgnoreCase);
        if (!isGpt && !isMbr)
        {
            report.Issues.Add(new BootabilityAuditIssue
            {
                Severity = BootabilityAuditStatus.Fail,
                Code = "UnsupportedPartitionStyle",
                Message = $"Unsupported partition style for Windows boot audit: {report.PartitionStyle}.",
                Remediation = "Convert or initialize the disk as GPT or MBR before expecting Windows bootability."
            });
        }

        var systemPartition = isGpt
            ? FindGptSystemPartition(partitions)
            : FindMbrSystemPartition(partitions);
        report.SystemPartitionFound = systemPartition is not null;
        report.SystemPartitionDriveLetter = systemPartition?.DriveLetter;

        if (windowsDrive is not { } detectedWindowsDrive)
        {
            report.Issues.Add(new BootabilityAuditIssue
            {
                Severity = BootabilityAuditStatus.Warning,
                Code = "NoWindowsInstall",
                Message = "No Windows installation was detected on the target disk.",
                Remediation = "If this was a data-only image or clone, no boot repair is required."
            });
            FinalizeReport(report);
            return report;
        }

        if (systemPartition is null)
        {
            report.Issues.Add(new BootabilityAuditIssue
            {
                Severity = BootabilityAuditStatus.Fail,
                Code = isGpt ? "MissingEfiSystemPartition" : "MissingActiveSystemPartition",
                Message = isGpt
                    ? "No EFI/System partition was detected on the restored or cloned GPT disk."
                    : "No active/system partition was detected on the restored or cloned MBR disk.",
                Remediation = "Run boot repair to create or repair boot files after confirming the restored Windows volume."
            });
        }
        else
        {
            report.BcdStoreFound = await CheckBcdStoreAsync(systemPartition, detectedWindowsDrive, isGpt, runner, log, ct);
            if (!report.BcdStoreFound)
            {
                report.Issues.Add(new BootabilityAuditIssue
                {
                    Severity = systemPartition.DriveLetter.HasValue ? BootabilityAuditStatus.Fail : BootabilityAuditStatus.Warning,
                    Code = "BcdStoreMissing",
                    Message = systemPartition.DriveLetter.HasValue
                        ? "Boot Configuration Data was not found on the detected system partition."
                        : "A system partition exists, but it has no drive letter so BCD files could not be inspected.",
                    Remediation = "Use boot repair to run bcdboot with the detected Windows volume and system partition."
                });
            }
        }

        await CheckWinReAsync(report, detectedWindowsDrive, runner, log, ct);
        report.SuggestedBootRepairPlan = BuildBootRepairPlan(report, isGpt, isMbr);
        FinalizeReport(report);
        return report;
    }

    private static async Task<char?> FindWindowsDriveAsync(
        IReadOnlyList<PartitionInfo> partitions,
        IProcessRunner runner,
        IActivityLog log,
        char? knownWindowsDrive,
        CancellationToken ct)
    {
        if (knownWindowsDrive.HasValue &&
            await LooksLikeWindowsInstallAsync(char.ToUpperInvariant(knownWindowsDrive.Value), runner, log, ct))
            return char.ToUpperInvariant(knownWindowsDrive.Value);

        foreach (var letter in partitions
                     .Where(p => p.DriveLetter.HasValue)
                     .Select(p => char.ToUpperInvariant(p.DriveLetter!.Value))
                     .Distinct()
                     .OrderBy(c => c))
        {
            if (await LooksLikeWindowsInstallAsync(letter, runner, log, ct))
                return letter;
        }

        return null;
    }

    private static Task<bool> LooksLikeWindowsInstallAsync(char driveLetter, IProcessRunner runner, IActivityLog log, CancellationToken ct) =>
        TestPathAsync($@"{driveLetter}:\Windows\System32\Config\SYSTEM", runner, log, ct);

    private static PartitionInfo? FindGptSystemPartition(IReadOnlyList<PartitionInfo> partitions) =>
        partitions.FirstOrDefault(p =>
            p.IsSystem ||
            p.Type.Contains("System", StringComparison.OrdinalIgnoreCase) ||
            p.Label.Contains("EFI", StringComparison.OrdinalIgnoreCase) ||
            (p.FileSystem.Equals("FAT32", StringComparison.OrdinalIgnoreCase) &&
             p.Size > 0 &&
             p.Size <= 1024L * 1024 * 1024));

    private static PartitionInfo? FindMbrSystemPartition(IReadOnlyList<PartitionInfo> partitions) =>
        partitions.FirstOrDefault(p => p.IsActive || p.IsSystem || p.IsBoot);

    private static async Task<bool> CheckBcdStoreAsync(
        PartitionInfo systemPartition,
        char windowsDrive,
        bool isGpt,
        IProcessRunner runner,
        IActivityLog log,
        CancellationToken ct)
    {
        if (systemPartition.DriveLetter is not { } systemLetter)
            return false;

        var path = isGpt
            ? $@"{char.ToUpperInvariant(systemLetter)}:\EFI\Microsoft\Boot\BCD"
            : $@"{char.ToUpperInvariant(systemLetter)}:\Boot\BCD";

        if (await TestPathAsync(path, runner, log, ct))
            return true;

        return !isGpt && systemLetter == windowsDrive &&
               await TestPathAsync($@"{windowsDrive}:\Boot\BCD", runner, log, ct);
    }

    private static async Task CheckWinReAsync(
        BootabilityAuditReport report,
        char windowsDrive,
        IProcessRunner runner,
        IActivityLog log,
        CancellationToken ct)
    {
        try
        {
            var output = await runner.RunExeAsync(
                "reagentc",
                $@"/info /target {windowsDrive}:\Windows",
                log,
                ignoreStderrOnSuccess: true,
                ct: ct);
            if (output.Contains("Enabled", StringComparison.OrdinalIgnoreCase))
            {
                report.WinReStatus = "Enabled";
                return;
            }

            if (output.Contains("Disabled", StringComparison.OrdinalIgnoreCase))
            {
                report.WinReStatus = "Disabled";
                report.Issues.Add(new BootabilityAuditIssue
                {
                    Severity = BootabilityAuditStatus.Warning,
                    Code = "WinReDisabled",
                    Message = "Windows Recovery Environment is disabled for the detected Windows installation.",
                    Remediation = $@"After confirming bootability, run reagentc /enable /osguid or repair WinRE for {windowsDrive}:\Windows."
                });
                return;
            }

            report.WinReStatus = "Unknown";
            report.Issues.Add(new BootabilityAuditIssue
            {
                Severity = BootabilityAuditStatus.Warning,
                Code = "WinReUnknown",
                Message = "Windows Recovery Environment status could not be determined from reagentc output.",
                Remediation = $@"Run reagentc /info /target {windowsDrive}:\Windows manually from an elevated shell."
            });
        }
        catch (Exception ex)
        {
            report.WinReStatus = "Unavailable";
            report.Issues.Add(new BootabilityAuditIssue
            {
                Severity = BootabilityAuditStatus.Warning,
                Code = "WinReCheckFailed",
                Message = $"Windows Recovery Environment status could not be checked: {ex.Message}",
                Remediation = $@"Run reagentc /info /target {windowsDrive}:\Windows after the disk is online."
            });
        }
    }

    private static async Task<bool> TestPathAsync(string path, IProcessRunner runner, IActivityLog log, CancellationToken ct)
    {
        var escaped = ProcessRunner.EscapePowerShellString(path);
        var output = await runner.RunPowerShellAsync(
            $"if (Test-Path -LiteralPath {escaped}) {{ 'true' }} else {{ 'false' }}",
            log,
            ct);
        return output.Contains("true", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildBootRepairPlan(BootabilityAuditReport report, bool isGpt, bool isMbr)
    {
        if (!report.WindowsDetected || report.WindowsDriveLetter is not { } windowsDrive)
            return "";

        if (isGpt)
        {
            var systemTarget = report.SystemPartitionDriveLetter.HasValue
                ? $"{char.ToUpperInvariant(report.SystemPartitionDriveLetter.Value)}:"
                : "<temporary EFI letter>:";
            return $@"1. Assign a temporary drive letter to the EFI System Partition if it does not already have one.
2. Run: bcdboot {windowsDrive}:\Windows /s {systemTarget} /f UEFI
3. Run: reagentc /info /target {windowsDrive}:\Windows and repair or enable WinRE if needed.
4. Remove any temporary EFI drive letter after verification.";
        }

        if (isMbr)
        {
            var systemTarget = report.SystemPartitionDriveLetter ?? windowsDrive;
            return $@"1. Ensure partition {systemTarget}: is marked active if this disk boots in BIOS mode.
2. Run: bcdboot {windowsDrive}:\Windows /s {systemTarget}: /f BIOS
3. Run: reagentc /info /target {windowsDrive}:\Windows and repair or enable WinRE if needed.";
        }

        return $@"Run bcdboot {windowsDrive}:\Windows only after converting the target disk to GPT or MBR and creating an appropriate system partition.";
    }

    private static void FinalizeReport(BootabilityAuditReport report)
    {
        report.Status = report.Issues.Any(i => i.Severity == BootabilityAuditStatus.Fail)
            ? BootabilityAuditStatus.Fail
            : report.Issues.Any(i => i.Severity == BootabilityAuditStatus.Warning)
                ? BootabilityAuditStatus.Warning
                : BootabilityAuditStatus.Pass;
    }
}
