namespace PartitionPilot.Tests;

public class BootabilityAuditServiceTests
{
    [Fact]
    public async Task AuditAsync_PassesForGptWindowsInstallWithEspBcdAndWinRe()
    {
        var disk = new DiskInfo { Number = 2, PartitionStyle = "GPT" };
        var partitions = new List<PartitionInfo>
        {
            new() { DiskNumber = 2, PartitionNumber = 1, DriveLetter = 'S', Type = "System", FileSystem = "FAT32", Label = "EFI", IsSystem = true, Size = 260L * 1024 * 1024 },
            new() { DiskNumber = 2, PartitionNumber = 2, DriveLetter = 'W', Type = "Basic", FileSystem = "NTFS", Label = "Windows" }
        };
        var runner = new FakeRunner(
            existingPaths:
            [
                @"W:\Windows\System32\Config\SYSTEM",
                @"S:\EFI\Microsoft\Boot\BCD"
            ],
            reagentcOutput: "Windows RE status: Enabled");

        var report = await BootabilityAuditService.AuditAsync(
            disk, partitions, runner, new TestLog(), knownWindowsDrive: 'W');

        Assert.Equal(BootabilityAuditStatus.Pass, report.Status);
        Assert.True(report.WindowsDetected);
        Assert.True(report.SystemPartitionFound);
        Assert.True(report.BcdStoreFound);
        Assert.Equal("Enabled", report.WinReStatus);
        Assert.Contains(@"bcdboot W:\Windows /s S: /f UEFI", report.SuggestedBootRepairPlan);
    }

    [Fact]
    public async Task AuditAsync_FailsGptWindowsInstallWithoutEsp()
    {
        var disk = new DiskInfo { Number = 3, PartitionStyle = "GPT" };
        var partitions = new List<PartitionInfo>
        {
            new() { DiskNumber = 3, PartitionNumber = 1, DriveLetter = 'W', Type = "Basic", FileSystem = "NTFS", Label = "Windows" }
        };
        var runner = new FakeRunner(
            existingPaths: [@"W:\Windows\System32\Config\SYSTEM"],
            reagentcOutput: "Windows RE status: Enabled");

        var report = await BootabilityAuditService.AuditAsync(
            disk, partitions, runner, new TestLog(), knownWindowsDrive: 'W');

        Assert.Equal(BootabilityAuditStatus.Fail, report.Status);
        Assert.Contains(report.Issues, i => i.Code == "MissingEfiSystemPartition");
        Assert.Contains("<temporary EFI letter>:", report.SuggestedBootRepairPlan);
    }

    [Fact]
    public async Task AuditAsync_WarnsForDataOnlyDiskWithoutWindowsInstall()
    {
        var disk = new DiskInfo { Number = 4, PartitionStyle = "GPT" };
        var partitions = new List<PartitionInfo>
        {
            new() { DiskNumber = 4, PartitionNumber = 1, DriveLetter = 'D', Type = "Basic", FileSystem = "NTFS", Label = "Data" }
        };
        var runner = new FakeRunner(existingPaths: [], reagentcOutput: "");

        var report = await BootabilityAuditService.AuditAsync(disk, partitions, runner, new TestLog());

        Assert.Equal(BootabilityAuditStatus.Warning, report.Status);
        Assert.False(report.WindowsDetected);
        Assert.Contains(report.Issues, i => i.Code == "NoWindowsInstall");
        Assert.Equal("", report.SuggestedBootRepairPlan);
    }

    [Fact]
    public async Task AuditAsync_FailsMbrWindowsInstallWithoutActiveSystemPartition()
    {
        var disk = new DiskInfo { Number = 5, PartitionStyle = "MBR" };
        var partitions = new List<PartitionInfo>
        {
            new() { DiskNumber = 5, PartitionNumber = 1, DriveLetter = 'C', Type = "Basic", FileSystem = "NTFS", Label = "Windows" }
        };
        var runner = new FakeRunner(
            existingPaths: [@"C:\Windows\System32\Config\SYSTEM"],
            reagentcOutput: "Windows RE status: Enabled");

        var report = await BootabilityAuditService.AuditAsync(
            disk, partitions, runner, new TestLog(), knownWindowsDrive: 'C');

        Assert.Equal(BootabilityAuditStatus.Fail, report.Status);
        Assert.Contains(report.Issues, i => i.Code == "MissingActiveSystemPartition");
        Assert.Contains(@"bcdboot C:\Windows", report.SuggestedBootRepairPlan);
    }

    private sealed class TestLog : IActivityLog
    {
        public void Log(string message) { }
    }

    private sealed class FakeRunner(IEnumerable<string> existingPaths, string reagentcOutput) : IProcessRunner
    {
        private readonly HashSet<string> _existingPaths = new(existingPaths, StringComparer.OrdinalIgnoreCase);

        public Task<string> RunDiskpartAsync(string script, IActivityLog? log = null, CancellationToken ct = default) =>
            Task.FromResult("");

        public Task<string> RunPowerShellAsync(string command, IActivityLog? log = null, CancellationToken ct = default)
        {
            var literal = ExtractSingleQuotedLiteral(command);
            return Task.FromResult(_existingPaths.Contains(literal) ? "true" : "false");
        }

        public Task<string> RunExeAsync(
            string fileName,
            string arguments,
            IActivityLog? log = null,
            bool ignoreStderrOnSuccess = false,
            CancellationToken ct = default)
        {
            Assert.Equal("reagentc", fileName);
            Assert.Contains("/info /target", arguments);
            return Task.FromResult(reagentcOutput);
        }

        private static string ExtractSingleQuotedLiteral(string command)
        {
            var start = command.IndexOf('\'');
            var end = command.IndexOf('\'', start + 1);
            Assert.True(start >= 0 && end > start, $"Unexpected Test-Path command: {command}");
            return command[(start + 1)..end].Replace("''", "'");
        }
    }
}
