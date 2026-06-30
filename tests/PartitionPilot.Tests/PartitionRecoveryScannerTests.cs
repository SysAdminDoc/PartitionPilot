using System.Text.Json;

namespace PartitionPilot.Tests;

public class PartitionRecoveryScannerTests
{
    [Fact]
    public void BuildFastProbeOffsets_IncludesLegacyAndOneMibBoundaries()
    {
        var offsets = PartitionRecoveryScanner.BuildFastProbeOffsets(5L * 1024 * 1024);

        Assert.Contains(0, offsets);
        Assert.Contains(63L * 512, offsets);
        Assert.Contains(1024L * 1024, offsets);
        Assert.Contains(4L * 1024 * 1024, offsets);
        Assert.DoesNotContain(5L * 1024 * 1024, offsets);
        Assert.Equal(offsets.Distinct().Count(), offsets.Count);
    }

    [Fact]
    public void DetectCandidate_DetectsNtfsVolumeBootRecord()
    {
        var buffer = new byte[4096];
        "NTFS    "u8.CopyTo(buffer.AsSpan(3));
        BitConverter.GetBytes((short)512).CopyTo(buffer, 0x0B);
        BitConverter.GetBytes(2048L).CopyTo(buffer, 0x28);
        buffer[510] = 0x55;
        buffer[511] = 0xAA;

        var candidate = PartitionRecoveryScanner.DetectCandidate(buffer, 1024L * 1024);

        Assert.NotNull(candidate);
        Assert.Equal("NTFS", candidate.FileSystem);
        Assert.Equal(1024L * 1024, candidate.Offset);
        Assert.Equal(2048L * 512, candidate.EstimatedSize);
    }

    [Fact]
    public void CoalesceCandidates_KeepsHighestConfidencePerFilesystemAndOffset()
    {
        var candidates = PartitionRecoveryScanner.CoalesceCandidates(
        [
            new CandidatePartition { Offset = 1024, FileSystem = "NTFS", Confidence = 70, Details = "low" },
            new CandidatePartition { Offset = 1024, FileSystem = "ntfs", Confidence = 95, Details = "high" },
            new CandidatePartition { Offset = 2048, FileSystem = "FAT32", Confidence = 80, Details = "other" }
        ]);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("high", candidates[0].Details);
        Assert.Equal(2048, candidates[1].Offset);
    }

    [Fact]
    public void FormatReport_IncludesModeCoverageCompletionAndResumeState()
    {
        var result = new RecoveryScanResult
        {
            DiskNumber = 2,
            DiskSize = 1024 * 1024,
            ScanMode = RecoveryScanMode.Deep.ToString(),
            CoverageBytes = 512 * 1024,
            CheckedOffsetCount = 1024,
            IsComplete = false,
            ResumeStatePath = @"C:\ProgramData\PartitionPilot\recovery\scan.json",
            ScannedAt = DateTimeOffset.Parse("2026-06-30T12:00:00Z"),
            Duration = TimeSpan.FromSeconds(3)
        };

        var report = PartitionRecoveryScanner.FormatReport(result);

        Assert.Contains("Mode: Deep", report);
        Assert.Contains("Coverage:", report);
        Assert.Contains("50.00%", report);
        Assert.Contains("Partial - resume state saved", report);
        Assert.Contains("Resume state:", report);
    }

    [Fact]
    public void FormatJson_IncludesModeCoverageAndCompletionState()
    {
        var result = new RecoveryScanResult
        {
            DiskNumber = 1,
            DiskSize = 4096,
            ScanMode = RecoveryScanMode.Fast.ToString(),
            CheckedOffsetCount = 4,
            CoverageBytes = 2048,
            IsComplete = true,
            ScannedAt = DateTimeOffset.Parse("2026-06-30T12:00:00Z")
        };

        using var doc = JsonDocument.Parse(PartitionRecoveryScanner.FormatJson(result));
        var root = doc.RootElement;

        Assert.Equal("Fast", root.GetProperty("ScanMode").GetString());
        Assert.Equal(2048, root.GetProperty("CoverageBytes").GetInt64());
        Assert.Equal(50, root.GetProperty("CoveragePercent").GetDouble());
        Assert.True(root.GetProperty("IsComplete").GetBoolean());
    }
}
