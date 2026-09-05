using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

namespace PartitionPilot.Tests;

// Reads the process-wide interface language, so it shares a collection with the tests that change it.
[Collection(LocalizationCollection.Name)]
public class LocExtensionTests
{
    private static readonly Regex DirectLiteralAttributePattern = new(
        "(?<![\\w.])(?<attr>AutomationProperties\\.Name|Content|Header|Text|Title|ToolTip)=\"(?<value>[^\"{}][^\"]*)\"",
        RegexOptions.Compiled);

    private static readonly Regex LocKeyPattern = new(
        "\\{local:Loc\\s+(?<key>[A-Za-z0-9_]+)\\}",
        RegexOptions.Compiled);

    [Theory]
    [InlineData("AppTitle", "PartitionPilot")]
    [InlineData("TabPartitions", "Partitions")]
    [InlineData("TabSnapshots", "Snapshots")]
    [InlineData("TabDiskHealth", "Disk Health")]
    [InlineData("TabTools", "Tools")]
    [InlineData("TabDiskImages", "Disk Images")]
    [InlineData("TabDiskUsage", "Disk Usage")]
    [InlineData("TabDiskCloning", "Disk Cloning")]
    [InlineData("Ready", "Ready")]
    [InlineData("ActivityLog", "Activity Log")]
    public void Get_ReturnsExpectedString(string key, string expected)
    {
        Assert.Equal(expected, LocExtension.Get(key));
    }

    [Fact]
    public void Get_ReturnsBracketedKey_ForMissingKey()
    {
        var result = LocExtension.Get("NonExistentKeyXyz");
        Assert.Equal("[NonExistentKeyXyz]", result);
    }

    [Theory]
    [InlineData("Refresh")]
    [InlineData("ExportLog")]
    [InlineData("SupportBundle")]
    [InlineData("Elevate")]
    [InlineData("Good")]
    [InlineData("Warning")]
    [InlineData("Error")]
    [InlineData("Cancel")]
    [InlineData("Apply")]
    [InlineData("OK")]
    public void Get_AllCommonKeys_ReturnNonEmpty(string key)
    {
        var result = LocExtension.Get(key);
        Assert.False(string.IsNullOrEmpty(result));
        Assert.DoesNotContain("[", result);
    }

    [Theory]
    [InlineData("TabPartitionsSubtitle")]
    [InlineData("TabSnapshotsSubtitle")]
    [InlineData("TabDiskHealthSubtitle")]
    [InlineData("TabToolsSubtitle")]
    [InlineData("TabDiskImagesSubtitle")]
    [InlineData("TabDiskUsageSubtitle")]
    [InlineData("TabDiskCloningSubtitle")]
    public void Get_AllSubtitleKeys_ReturnNonEmpty(string key)
    {
        var result = LocExtension.Get(key);
        Assert.False(string.IsNullOrEmpty(result));
        Assert.DoesNotContain("[", result);
    }

    [Theory]
    [InlineData("Temperature")]
    [InlineData("WearUsed")]
    [InlineData("ReallocatedSectors")]
    [InlineData("PendingSectors")]
    [InlineData("NvmeAvailableSpare")]
    [InlineData("NvmeMediaErrors")]
    [InlineData("NotAvailable")]
    public void Get_SmartLabels_ReturnNonEmpty(string key)
    {
        var result = LocExtension.Get(key);
        Assert.False(string.IsNullOrEmpty(result));
    }

    /// <summary>
    /// The snapshot browser's messages moved from source literals into resources. English has to come back
    /// out byte for byte, newlines included, or the conversion quietly reworded the app.
    /// </summary>
    [Theory]
    [InlineData("RestoreSnapshotTitle", "", "", "Restore Snapshot")]
    [InlineData("RestoreCannotProceed", "no room", "", "Restore cannot proceed:\nno room")]
    [InlineData("RestoreNotRecreatedHeading", "Recovery", "", "\n\nNot recreated by this plan:\nRecovery")]
    [InlineData("RestoreFinalPrompt", "3", "", "FINAL CONFIRMATION: All data on Disk 3 will be permanently destroyed and its partitions recreated from the snapshot.")]
    [InlineData("RestoreComplete", "3", " and more", "Partition table restored onto Disk 3. and more")]
    [InlineData("RestoreStoppedAfterClear", "3", "", "Restore stopped after Disk 3 was already cleared, so it currently has no usable partition table. Re-run the restore once the identity check passes; do not power off in between.")]
    [InlineData("DiskNotConnected", "3", "", "Disk 3 is not currently connected.")]
    [InlineData("RestoreBlocked", "locked", "", "Restore blocked:\nlocked")]
    [InlineData("SnapshotLoadFailed", "boom", "", "Failed to load snapshots:\nboom")]
    [InlineData("SnapshotCompareFailed", "boom", "", "Failed to compare snapshot:\nboom")]
    [InlineData("SnapshotExported", "C:\\out.json", "", "Snapshot exported to:\nC:\\out.json")]
    [InlineData("SnapshotExportFailed", "boom", "", "Failed to export snapshot:\nboom")]
    [InlineData("RecoveryPlanExported", "C:\\plan.txt", "", "Recovery plan exported to:\nC:\\plan.txt")]
    [InlineData("RecoveryPlanExportFailed", "boom", "", "Failed to export recovery plan:\nboom")]
    [InlineData("SnapshotsNoneFound", "C:\\backups", "", "No snapshots found in C:\\backups.")]
    [InlineData("SnapshotsLoaded", "4", "C:\\backups", "4 snapshot(s) loaded from C:\\backups.")]
    [InlineData("SnapshotSelectedHint", "disk0.json", "", "Selected disk0.json. Click Compare Current Layout to inspect drift.")]
    [InlineData("SnapshotsRefreshPrompt", "", "", "Refresh to load saved partition table snapshots.")]
    [InlineData("SnapshotSelectPrompt", "", "", "Select a snapshot and compare it with the current disk layout.")]
    [InlineData("RecoveryGuidanceCopied", "", "", "Recovery guidance copied to the clipboard.")]
    [InlineData("HexSelectDiskPrompt", "", "", "Select a disk and read a sector.")]
    [InlineData("UsageScanCancelledSummary", "", "", "Disk usage scan cancelled.")]
    [InlineData("UsageScanFailedSummary", "boom", "", "Scan failed: boom")]
    [InlineData("HealthSectorSizes", "512", "4096", "Logical: 512 / Physical: 4096")]
    [InlineData("HealthTemperatureC", "38", "", "38 C")]
    [InlineData("HealthWearUsed", "4", "", "4% used")]
    [InlineData("HealthPowerOnHours", "1,204", "", "1,204 hours")]
    [InlineData("HealthErrorsTotal", "7", "5", "7 total (5 corrected)")]
    [InlineData("HealthMilliseconds", "12", "", "12 ms")]
    [InlineData("HealthPercentValue", "99", "", "99%")]
    [InlineData("HealthMinutes", "310", "", "310 min")]
    [InlineData("SmartMetadataVersion", "2", "", "SMART metadata 2")]
    [InlineData("HistoryMany", "5", "", "5 readings")]
    [InlineData("EnduranceUnrated", "12.5", "", "12.5 TB written (set rated TBW for endurance gauge)")]
    public void Format_ReproducesTheEnglishTextItReplaced(string key, string first, string second, string expected)
    {
        Assert.Equal(expected, LocExtension.Format(key, first, second));
    }

    [Fact]
    public void RestoreWarningPrompt_ReproducesTheEnglishTextItReplaced()
    {
        var actual = LocExtension.Format("RestoreWarningPrompt", 3, "summary", "plan", "extra");

        Assert.Equal(
            "WARNING: Restoring this snapshot clears Disk 3 and recreates its partition table.\n\n" +
            "Target:\nsummary\n\nplanextra\n\nContinue?",
            actual);
    }

    [Fact]
    public void RestoreFailedPartway_ReproducesTheEnglishTextItReplaced()
    {
        var actual = LocExtension.Format("RestoreFailedPartway", "boom", 3);

        Assert.Equal(
            "Restore failed partway through:\nboom\n\n" +
            "Disk 3 has already been cleared and its partition table is incomplete. " +
            "Do not power off. Re-run the restore once the cause is resolved.",
            actual);
    }

    /// <summary>
    /// The partition confirmations are the ones that stand between the operator and a wiped disk, so their
    /// English wording is pinned exactly rather than merely checked for being non-empty.
    /// </summary>
    [Fact]
    public void PartitionConfirmations_ReproduceTheEnglishTextTheyReplaced()
    {
        Assert.Equal(
            "Delete partition 2 on Disk 1?\nTarget:\nsummary\n\nLetter: E:, Size: 100 GB\n\n" +
            "ALL DATA ON THIS PARTITION WILL BE LOST.\n\n" +
            "The deletion will be queued and applied when you click Apply.",
            LocExtension.Format("ConfirmDeletePrompt", 2, 1, "summary", "E:", "100 GB", ""));

        Assert.Equal(
            "CRITICAL: Partition 2 is a Recovery partition (Boot).\n\n" +
            "Deleting it may make the system unbootable.\n\nTarget:\nsummary\n\n" +
            "Letter: E:, Size: 100 GB\n\nType YES to confirm this destructive action.",
            LocExtension.Format("DeleteCriticalPrompt", 2, "Recovery",
                LocExtension.Get("PartitionFlagBoot"), "summary", "E:", "100 GB", ""));

        Assert.Equal(
            "Set partition 2 on Disk 1 as ACTIVE?\n\n" +
            "Warning: Setting the wrong partition as active can prevent Windows from booting.",
            LocExtension.Format("SetActivePrompt", 2, 1));

        Assert.Equal("Hide partition 2 (E:) on Disk 1?", LocExtension.Format("HidePartitionPrompt", 2, "E:", 1));
        Assert.Equal("Unhide partition 2 (E:) on Disk 1?", LocExtension.Format("UnhidePartitionPrompt", 2, "E:", 1));

        Assert.Equal(
            "Extend partition 2 (E:) on Disk 1 to fill all adjacent unallocated space?",
            LocExtension.Format("ExtendPrompt", 2, "E:", 1));

        Assert.Equal(
            "Initialize Disk 1 (Samsung, 1 TB) with a GPT partition table?\n\nidentity\n\n" +
            "This will write an empty GPT partition table to the disk. " +
            "Use MBR only if legacy BIOS boot compatibility is required.",
            LocExtension.Format("InitializeDiskPrompt", 1, "Samsung", "1 TB", "identity"));
    }

    [Fact]
    public void PartitionGuards_ReproduceTheEnglishTextTheyReplaced()
    {
        Assert.Equal(
            "Delete partition 2 targets:\nsummary\n\nThis disk belongs to Storage Spaces pool \"Pool1\".\n\n" +
            "Modifying pooled disks can break pool integrity and cause data loss across the entire pool. " +
            "Use Windows Storage Spaces management to modify pooled disks.",
            LocExtension.Format("StoragePoolGuardBody",
                LocExtension.Format("OpDeletePartition", 2), "summary", "Pool1"));

        Assert.Equal(
            "Format E: targets a Linux partition that PartitionPilot cannot manage natively.\n\n" +
            "Partition: Disk 1 Partition 2, Size: 100 GB\n\n" +
            "Modifying this partition may destroy data that Windows cannot read or recover. " +
            "Proceed only if you are certain this partition is no longer needed.",
            LocExtension.Format("UnsupportedTypeBody",
                LocExtension.Format("OpFormatDrive", 'E'), "Linux", "Disk 1 Partition 2", "100 GB"));

        Assert.Equal(
            "PartitionPilot will not delete a Recovery partition automatically because that can disable " +
            "Windows RE or remove the recovery environment.\n\n" +
            "Use a dedicated recovery relocation workflow before changing this partition.\n\nreagentc output",
            LocExtension.Format("RecoveryGuardBody", LocExtension.Get("VerbDelete"), "reagentc output"));
    }

    [Fact]
    public void EnduranceRated_ReproducesTheEnglishTextItReplaced()
    {
        Assert.Equal(
            "12.5 TB written of 300 TB rated (4.2% consumed)",
            LocExtension.Format("EnduranceRated", "12.5", "300", "4.2"));
    }

    [Fact]
    public void UsageScanSummary_ReproducesTheEnglishTextItReplaced()
    {
        var actual = LocExtension.Format("UsageScanSummary", 12, "MFT", "4.0 GB", "1.5");

        Assert.Equal("Scanned 12 top-level folders via MFT. Total: 4.0 GB in 1.5s", actual);
    }

    [Fact]
    public void PseudoLocale_HasAllKeysFromDefaultResources()
    {
        var asm = typeof(LocExtension).Assembly;
        var rm = new ResourceManager("PartitionPilot.Properties.Strings", asm);

        var defaultSet = rm.GetResourceSet(CultureInfo.InvariantCulture, true, false);
        Assert.NotNull(defaultSet);

        var pseudoSet = rm.GetResourceSet(new CultureInfo("qps-ploc"), true, false);

        var defaultKeys = new List<string>();
        var enumerator = defaultSet!.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (enumerator.Key is string key)
                defaultKeys.Add(key);
        }

        Assert.True(defaultKeys.Count >= 480, $"Expected >=480 resource keys, found {defaultKeys.Count}");

        if (pseudoSet is not null)
        {
            var pseudoKeys = new HashSet<string>();
            var pseudoEnum = pseudoSet.GetEnumerator();
            while (pseudoEnum.MoveNext())
            {
                if (pseudoEnum.Key is string key)
                    pseudoKeys.Add(key);
            }

            var missing = defaultKeys.Where(k => !pseudoKeys.Contains(k)).ToList();
            Assert.True(missing.Count == 0,
                $"Pseudo-locale is missing {missing.Count} key(s): {string.Join(", ", missing.Take(10))}");
        }
    }

    [Fact]
    public void Xaml_UserVisibleAttributes_UseLocExtension()
    {
        var root = FindRepoRoot();
        var xamlFiles = Directory.GetFiles(Path.Combine(root, "src", "PartitionPilot"), "*.xaml", SearchOption.AllDirectories);
        var violations = new List<string>();

        foreach (var file in xamlFiles)
        {
            var relative = Path.GetRelativePath(root, file);
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in DirectLiteralAttributePattern.Matches(lines[i]))
                {
                    violations.Add($"{relative}:{i + 1}:{match.Groups["attr"].Value}=\"{match.Groups["value"].Value}\"");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Hardcoded XAML user-visible or automation string(s) must use LocExtension: " +
            string.Join("; ", violations.Take(10)));
    }

    [Fact]
    public void Xaml_LocExtensionKeys_ExistInResources()
    {
        var root = FindRepoRoot();
        var xamlFiles = Directory.GetFiles(Path.Combine(root, "src", "PartitionPilot"), "*.xaml", SearchOption.AllDirectories);
        var asm = typeof(LocExtension).Assembly;
        var rm = new ResourceManager("PartitionPilot.Properties.Strings", asm);
        var missing = new List<string>();

        foreach (var file in xamlFiles)
        {
            var relative = Path.GetRelativePath(root, file);
            var text = File.ReadAllText(file);
            foreach (Match match in LocKeyPattern.Matches(text))
            {
                var key = match.Groups["key"].Value;
                if (rm.GetString(key, CultureInfo.InvariantCulture) is null)
                {
                    missing.Add($"{relative}:{key}");
                }
            }
        }

        Assert.True(missing.Count == 0,
            "XAML references missing localization key(s): " + string.Join("; ", missing.Take(10)));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "README.md")) &&
                Directory.Exists(Path.Combine(dir.FullName, "src", "PartitionPilot")) &&
                Directory.Exists(Path.Combine(dir.FullName, "tests", "PartitionPilot.Tests")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate PartitionPilot repository root.");
    }
}
