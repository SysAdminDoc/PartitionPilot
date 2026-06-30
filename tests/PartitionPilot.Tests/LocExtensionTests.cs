using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

namespace PartitionPilot.Tests;

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
