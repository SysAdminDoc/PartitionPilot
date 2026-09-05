namespace PartitionPilot.Tests;

public class ShrinkBlockerServiceTests
{
    /// <summary>
    /// Verbatim Microsoft-Windows-Defrag event 259 captured from Windows 11 26200 on 2026-09-04.
    /// The blocker is shadow copy storage: the second GUID is the VSS storage identifier.
    /// </summary>
    private const string ShadowCopyEvent = """
        A volume shrink analysis was initiated on volume (C:). This event log entry details information about the last unmovable file that could limit the maximum number of reclaimable bytes.

         Diagnostic details:
         - The last unmovable file appears to be: \System Volume Information\{98c40654-98ef-11f1-9672-acb480fe9af5}{3808876b-c176-4e48-b7ae-04046e6cc752}::$DATA
         - The last cluster of the file is: 0x10b5005e
         - Shrink potential target (LCN address): 0x10b106c2
         - The NTFS file flags are: ---AD
         - Shrink phase: <analysis>

         To find more details about this file please use the "fsutil volume querycluster \\?\Volume{ae5f7940-eaa7-4ed7-9f69-e190a0644a13} 0x10b5005e" command.
        """;

    /// <summary>A second real capture from the same machine, blocked by the NTFS change journal instead.</summary>
    private const string UsnJournalEvent = """
        A volume shrink analysis was initiated on volume (C:). This event log entry details information about the last unmovable file that could limit the maximum number of reclaimable bytes.

         Diagnostic details:
         - The last unmovable file appears to be: \$Extend\$UsnJrnl:$J:$DATA
         - The last cluster of the file is: 0xcf3210
         - Shrink potential target (LCN address): 0xd06c4e
         - The NTFS file flags are: -S--D
         - Shrink phase: <analysis>

         To find more details about this file please use the "fsutil volume querycluster \\?\Volume{ae5f7940-eaa7-4ed7-9f69-e190a0644a13} 0xcf3210" command.
        """;

    [Fact]
    public void ParseEvent259_ReadsEveryFieldFromARealShadowCopyRecord()
    {
        var blocker = ShrinkBlockerService.ParseEvent259(ShadowCopyEvent);

        Assert.NotNull(blocker);
        Assert.Equal(
            @"\System Volume Information\{98c40654-98ef-11f1-9672-acb480fe9af5}{3808876b-c176-4e48-b7ae-04046e6cc752}::$DATA",
            blocker!.FilePath);
        Assert.Equal(0x10b5005e, blocker.LastClusterOfFile);
        Assert.Equal(0x10b106c2, blocker.ShrinkTargetLcn);
        Assert.Equal("---AD", blocker.NtfsFileFlags);
        Assert.Equal(@"\\?\Volume{ae5f7940-eaa7-4ed7-9f69-e190a0644a13}", blocker.VolumeGuidPath);
        Assert.Equal(ShrinkBlockerKind.ShadowCopyStorage, blocker.Kind);
    }

    [Fact]
    public void ParseEvent259_ComputesTheSpanTheBlockerIsHoldingBack()
    {
        var blocker = ShrinkBlockerService.ParseEvent259(ShadowCopyEvent)!;

        Assert.Equal(0x10b5005e - 0x10b106c2, blocker.BlockedClusters);
        Assert.Equal((0x10b5005e - 0x10b106c2) * 4096L, blocker.BlockedBytes(4096));
    }

    [Fact]
    public void ParseEvent259_ClassifiesTheChangeJournalRecord()
    {
        var blocker = ShrinkBlockerService.ParseEvent259(UsnJournalEvent);

        Assert.NotNull(blocker);
        Assert.Equal(@"\$Extend\$UsnJrnl:$J:$DATA", blocker!.FilePath);
        Assert.Equal(ShrinkBlockerKind.UsnJournal, blocker.Kind);
        Assert.Contains("fsutil usn deletejournal", blocker.Remedy);
    }

    [Fact]
    public void ParseEvent259_ReproducesTheFsutilCommandWindowsPrints()
    {
        var blocker = ShrinkBlockerService.ParseEvent259(ShadowCopyEvent)!;

        Assert.Equal(
            @"fsutil volume querycluster \\?\Volume{ae5f7940-eaa7-4ed7-9f69-e190a0644a13} 0x10b5005e",
            blocker.QueryClusterCommand);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("The storage optimizer successfully completed defragmentation on (C:)")]
    public void ParseEvent259_ReturnsNothingForTextThatIsNotAShrinkAnalysis(string? message)
    {
        Assert.Null(ShrinkBlockerService.ParseEvent259(message));
    }

    [Fact]
    public void ParseEvent259_ReturnsNothingWhenTheClusterFieldsAreMissing()
    {
        var truncated = ShadowCopyEvent[..ShadowCopyEvent.IndexOf("- The last cluster", StringComparison.Ordinal)];

        Assert.Null(ShrinkBlockerService.ParseEvent259(truncated));
    }

    [Fact]
    public void ParseVolumeLetter_ReadsTheVolumeTheAnalysisRanAgainst()
    {
        Assert.Equal('C', ShrinkBlockerService.ParseVolumeLetter(ShadowCopyEvent));
        Assert.Null(ShrinkBlockerService.ParseVolumeLetter("no volume here"));
    }

    [Theory]
    [InlineData(@"\pagefile.sys", ShrinkBlockerKind.PageFile)]
    [InlineData(@"\swapfile.sys", ShrinkBlockerKind.PageFile)]
    [InlineData(@"\hiberfil.sys", ShrinkBlockerKind.HibernationFile)]
    [InlineData(@"\$Extend\$UsnJrnl:$J:$DATA", ShrinkBlockerKind.UsnJournal)]
    [InlineData(@"\$Mft::$DATA", ShrinkBlockerKind.NtfsMetadata)]
    [InlineData(@"\$Bitmap::$DATA", ShrinkBlockerKind.NtfsMetadata)]
    [InlineData(@"\ProgramData\Microsoft\Search\Data\Applications\Windows\Windows.edb", ShrinkBlockerKind.SearchIndex)]
    [InlineData(@"\Users\someone\big-video.mkv", ShrinkBlockerKind.Unknown)]
    public void Classify_MapsAPathToTheRemedyThatApplies(string path, ShrinkBlockerKind expected)
    {
        Assert.Equal(expected, ShrinkBlockerService.Classify(path));
    }

    [Fact]
    public void Classify_DistinguishesShadowCopyStorageFromOtherSystemVolumeInformationFiles()
    {
        Assert.Equal(ShrinkBlockerKind.ShadowCopyStorage, ShrinkBlockerService.Classify(
            @"\System Volume Information\{aaaa}{3808876b-c176-4e48-b7ae-04046e6cc752}::$DATA"));

        Assert.Equal(ShrinkBlockerKind.NtfsMetadata, ShrinkBlockerService.Classify(
            @"\System Volume Information\tracking.log"));
    }

    [Fact]
    public void FormatReport_NamesTheFileTheSpanAndTheRemedy()
    {
        var blocker = ShrinkBlockerService.ParseEvent259(ShadowCopyEvent)!;

        var report = ShrinkBlockerService.FormatReport(blocker, 4096, currentMinimumSize: 200L * 1024 * 1024 * 1024);

        Assert.Contains("System Volume Information", report);
        Assert.Contains("Shadow copy storage", report);
        Assert.Contains("Blocked span:", report);
        Assert.Contains("vssadmin delete shadows", report);
        Assert.Contains("fsutil volume querycluster", report);
        Assert.Contains("shrink cannot go below", report);
    }

    [Fact]
    public async Task FindLatestBlockerAsync_PicksTheFirstRecordForTheRequestedVolume()
    {
        var runner = new FakeRunner($"===EVENT==={UsnJournalEvent}\n===EVENT==={ShadowCopyEvent}");

        var blocker = await ShrinkBlockerService.FindLatestBlockerAsync(
            'c', runner, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(blocker);
        Assert.Equal(ShrinkBlockerKind.UsnJournal, blocker!.Kind);
    }

    [Fact]
    public async Task FindLatestBlockerAsync_IgnoresRecordsForOtherVolumes()
    {
        var runner = new FakeRunner($"===EVENT==={ShadowCopyEvent.Replace("volume (C:)", "volume (D:)")}");

        Assert.Null(await ShrinkBlockerService.FindLatestBlockerAsync(
            'c', runner, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindLatestBlockerAsync_ReturnsNothingWhenTheLogCannotBeRead()
    {
        var runner = new ThrowingRunner();

        Assert.Null(await ShrinkBlockerService.FindLatestBlockerAsync(
            'c', runner, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindLatestBlockerAsync_RejectsANonLetterDrive()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ShrinkBlockerService.FindLatestBlockerAsync('1', new FakeRunner("")));
    }

    private sealed class FakeRunner(string output) : IProcessRunner
    {
        public Task<string> RunDiskpartAsync(string script, IActivityLog? log = null, CancellationToken ct = default) =>
            Task.FromResult("");

        public Task<string> RunPowerShellAsync(string command, IActivityLog? log = null, CancellationToken ct = default) =>
            Task.FromResult(output);

        public Task<string> RunExeAsync(string fileName, string arguments, IActivityLog? log = null,
            bool ignoreStderrOnSuccess = false, CancellationToken ct = default) => Task.FromResult("");
    }

    private sealed class ThrowingRunner : IProcessRunner
    {
        public Task<string> RunDiskpartAsync(string script, IActivityLog? log = null, CancellationToken ct = default) =>
            Task.FromResult("");

        public Task<string> RunPowerShellAsync(string command, IActivityLog? log = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("No matching events were found.");

        public Task<string> RunExeAsync(string fileName, string arguments, IActivityLog? log = null,
            bool ignoreStderrOnSuccess = false, CancellationToken ct = default) => Task.FromResult("");
    }
}
