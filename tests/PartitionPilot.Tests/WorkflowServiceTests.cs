using System.IO.Compression;

namespace PartitionPilot.Tests;

public class WorkflowServiceTests
{
    [Fact]
    public void PartitionPlanService_BuildsFormatPlanAndSanitizesLabel()
    {
        var plan = PartitionPlanService.Build(new PartitionPlanRequest(
            "format",
            2,
            4,
            "NTFS",
            "Data; & Bad",
            null,
            null,
            Apply: false));

        Assert.Equal("format", plan.Operation);
        Assert.Equal("High", plan.RiskLevel);
        Assert.Contains("select disk 2", plan.DiskpartScript);
        Assert.Contains("select partition 4", plan.DiskpartScript);
        Assert.Contains("format fs=NTFS label=\"Data  Bad\" quick", plan.DiskpartScript);
    }

    [Fact]
    public void PartitionPlanService_RejectsUnsupportedCreateFilesystem()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            PartitionPlanService.Build(new PartitionPlanRequest(
                "create",
                2,
                null,
                "APFS",
                null,
                null,
                "10GB",
                Apply: false)));

        Assert.Contains("APFS", ex.Message);
    }

    [Fact]
    public void WipeWorkflowService_BuildsDestructivePromptSequence()
    {
        var disk = new DiskIdentitySnapshot
        {
            DiskNumber = 3,
            FriendlyName = "Target Disk",
            Size = 1000,
            PartitionStyle = "GPT"
        };

        var prompts = WipeWorkflowService.BuildFullDiskPrompts(disk, "SinglePass", disk.Size);

        Assert.Equal(3, prompts.Count);
        Assert.False(prompts[0].IsDanger);
        Assert.True(prompts[^1].IsDanger);
        Assert.Contains("Target Disk", prompts[0].Message);
        Assert.Contains("SinglePass", prompts[1].Message);
    }

    [Fact]
    public void CloneWorkflowService_BuildsPromptsAndSummary()
    {
        var source = new DiskIdentitySnapshot { DiskNumber = 1, FriendlyName = "Source", Size = 1000, PartitionStyle = "GPT" };
        var destination = new DiskIdentitySnapshot { DiskNumber = 2, FriendlyName = "Destination", Size = 1000, PartitionStyle = "GPT" };

        var prompts = CloneWorkflowService.BuildSectorClonePrompts(source, destination);
        var summary = CloneWorkflowService.BuildCompletionSummary(1, 2, "clone ok", "boot ok");

        Assert.Equal(2, prompts.Count);
        Assert.All(prompts, prompt => Assert.True(prompt.IsDanger));
        Assert.Contains("Source", prompts[0].Message);
        Assert.Contains("Destination", prompts[0].Message);
        Assert.Contains("Disk 1 -> Disk 2", summary);
        Assert.Contains("boot ok", summary);
    }

    [Fact]
    public async Task SupportBundleService_CreatesRedactedZip()
    {
        var root = CreateTempDir();
        try
        {
            var snapshots = Path.Combine(root, "snapshots");
            Directory.CreateDirectory(snapshots);
            File.WriteAllText(Path.Combine(snapshots, "snapshot.json"), """{"SerialNumber":"SN123","Path":"C:\\Users\\Alice\\disk.json"}""");

            var zipPath = Path.Combine(root, "support.zip");
            await SupportBundleService.CreateAsync(
                new SupportBundleOptions(
                    zipPath,
                    "PartitionPilot v1.2.3",
                    "Administrator",
                    "Mounted C:\\Users\\Alice\\Desktop\\disk.vhdx\nSerial: ABC123",
                    snapshots,
                    IsAdmin: true,
                    new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero)),
                new FakeWmiDiskService(),
                TestContext.Current.CancellationToken);

            using var archive = ZipFile.OpenRead(zipPath);
            var names = archive.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("system-info.json", names);
            Assert.Contains("activity-log.txt", names);
            Assert.Contains("disk-summary.json", names);
            Assert.Contains("snapshots/snapshot.json", names);

            var log = ReadEntry(archive, "activity-log.txt");
            var snapshot = ReadEntry(archive, "snapshots/snapshot.json");
            Assert.DoesNotContain("Alice", log);
            Assert.DoesNotContain("ABC123", log);
            Assert.Contains("Serial: [redacted]", log);
            Assert.DoesNotContain("SN123", snapshot);
            Assert.Contains("\"SerialNumber\":\"[redacted]\"", snapshot);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException($"Missing {name}");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pp-workflows-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private sealed class FakeWmiDiskService : IWmiDiskService
    {
        public Task<List<DiskInfo>> GetDisksAsync() => Task.FromResult(new List<DiskInfo>
        {
            new() { Number = 0, FriendlyName = "Disk", Size = 1024, PartitionStyle = "GPT", NumberOfPartitions = 1 }
        });

        public Task<List<PartitionInfo>> GetPartitionsAsync(int diskNumber) => Task.FromResult(new List<PartitionInfo>());
        public Task<List<VolumeInfo>> GetVolumesAsync() => Task.FromResult(new List<VolumeInfo>());
        public Task<List<PhysicalDiskInfo>> GetPhysicalDisksAsync() => Task.FromResult(new List<PhysicalDiskInfo>());
        public Task<SmartData?> GetSmartDataAsync(string deviceId) => Task.FromResult<SmartData?>(null);
        public Task<List<AlignmentInfo>> GetAlignmentAuditAsync() => Task.FromResult(new List<AlignmentInfo>());
        public Task<HashSet<char>> GetPagefileLocationsAsync() => Task.FromResult(new HashSet<char>());
        public Task<List<char>> GetAvailableLettersAsync() => Task.FromResult(new List<char>());
        public Task<(long Min, long Max)> GetPartitionSupportedSizeAsync(char driveLetter) => Task.FromResult((0L, 0L));
        public Task<List<MountedImageInfo>> GetMountedImagesAsync() => Task.FromResult(new List<MountedImageInfo>());
        public Task<Dictionary<char, string>> GetBitLockerStatusAsync() => Task.FromResult(new Dictionary<char, string>());
        public Task<List<string>> GetBitLockerProtectedTargetsAsync(int diskNumber) => Task.FromResult(new List<string>());
        public Task<Dictionary<int, string>> GetStoragePoolMembershipAsync() => Task.FromResult(new Dictionary<int, string>());
        public Task<Dictionary<string, (string Health, string Status, bool ReadOnly)>> GetStoragePoolHealthAsync() =>
            Task.FromResult(new Dictionary<string, (string Health, string Status, bool ReadOnly)>());
    }
}
