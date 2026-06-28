namespace PartitionPilot.Tests;

public class LayoutDiffServiceTests
{
    [Theory]
    [InlineData("GPT")]
    [InlineData("mbr")]
    public void ComputeDiff_AllowsKnownPartitionStyles(string style)
    {
        var diff = LayoutDiffService.ComputeDiff(
            new PartitionLayoutSpec
            {
                Style = style,
                Partitions = [new PartitionSpec { UseMaximumSize = true, FileSystem = "NTFS", DriveLetter = "d:" }]
            },
            RawDisk(),
            []);

        Assert.Contains(diff, entry => entry.DiskpartScript.Contains("convert ", StringComparison.Ordinal));
        Assert.Contains(diff, entry => entry.DiskpartScript.Contains("assign letter=D", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("GPT\nclean")]
    [InlineData("GPT & clean")]
    [InlineData("dynamic")]
    [InlineData("")]
    public void ComputeDiff_RejectsInvalidOrInjectedPartitionStyle(string style)
    {
        var spec = ValidSpec();
        spec.Style = style;

        var ex = Assert.Throws<ArgumentException>(() =>
            LayoutDiffService.ComputeDiff(spec, RawDisk(), []));

        Assert.Contains("style", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("1024\nclean")]
    [InlineData("1024 & clean")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("12.5")]
    [InlineData("")]
    public void ComputeDiff_RejectsInvalidOrInjectedSizeMb(string sizeMb)
    {
        var spec = ValidSpec();
        spec.Partitions[0].SizeMB = sizeMb;

        var ex = Assert.Throws<ArgumentException>(() =>
            LayoutDiffService.ComputeDiff(spec, RawDisk(), []));

        Assert.Contains("SizeMB", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("C\nclean")]
    [InlineData("C & clean")]
    [InlineData("AA")]
    [InlineData("1")]
    public void ComputeDiff_RejectsInvalidOrInjectedDriveLetter(string driveLetter)
    {
        var spec = ValidSpec();
        spec.Partitions[0].DriveLetter = driveLetter;

        var ex = Assert.Throws<ArgumentException>(() =>
            LayoutDiffService.ComputeDiff(spec, RawDisk(), []));

        Assert.Contains("DriveLetter", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeDiff_EmitsNormalizedSafeScriptForValidSpec()
    {
        var diff = LayoutDiffService.ComputeDiff(
            new PartitionLayoutSpec
            {
                Style = " gpt ",
                Partitions =
                [
                    new PartitionSpec
                    {
                        SizeMB = " 1024 ",
                        FileSystem = "ntfs",
                        Label = "Data\";&",
                        DriveLetter = "e:"
                    }
                ]
            },
            RawDisk(),
            []);

        var create = Assert.Single(diff.Where(entry => entry.Action == "Create"));
        Assert.Contains("create partition primary size=1024", create.DiskpartScript, StringComparison.Ordinal);
        Assert.Contains("format fs=ntfs label=\"Data\" quick", create.DiskpartScript, StringComparison.Ordinal);
        Assert.Contains("assign letter=E", create.DiskpartScript, StringComparison.Ordinal);
        Assert.DoesNotContain(";&", create.DiskpartScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeDiff_RequiresSizeUnlessUseMaximumSizeIsTrue()
    {
        var spec = ValidSpec();
        spec.Partitions[0].SizeMB = null;

        var ex = Assert.Throws<ArgumentException>(() =>
            LayoutDiffService.ComputeDiff(spec, RawDisk(), []));

        Assert.Contains("UseMaximumSize", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeDiff_ReturnsNoOpWhenCurrentLayoutMatchesSpec()
    {
        var diff = LayoutDiffService.ComputeDiff(
            ValidSpec(),
            GptDisk(),
            [MatchingPartition()]);

        Assert.Empty(diff);
        Assert.Contains("No changes needed", LayoutDiffService.FormatPlan(diff), StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeDiff_CreateOnlyWhenExistingPrefixMatchesSpec()
    {
        var spec = ValidSpec();
        spec.Partitions.Add(new PartitionSpec
        {
            SizeMB = "2048",
            FileSystem = "NTFS",
            Label = "Logs",
            DriveLetter = "L"
        });

        var diff = LayoutDiffService.ComputeDiff(spec, GptDisk(), [MatchingPartition()]);

        var create = Assert.Single(diff);
        Assert.Equal("Create", create.Action);
        Assert.DoesNotContain("clean", create.DiskpartScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("size=2048", create.DiskpartScript, StringComparison.Ordinal);
        Assert.Contains("assign letter=L", create.DiskpartScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeDiff_BlocksMismatchedPopulatedDiskWithoutReplace()
    {
        var current = MatchingPartition();
        current.Label = "Other";

        var diff = LayoutDiffService.ComputeDiff(ValidSpec(), GptDisk(), [current]);

        var blocked = Assert.Single(diff);
        Assert.Equal("Blocked", blocked.Action);
        Assert.Equal("Blocked", blocked.RiskLevel);
        Assert.Empty(blocked.DiskpartScript);
        Assert.Contains("--replace", blocked.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeDiff_ReplacePlanClearsAndRecreatesMismatchedDiskOnlyWhenAllowed()
    {
        var current = MatchingPartition();
        current.Label = "Other";

        var diff = LayoutDiffService.ComputeDiff(
            ValidSpec(),
            GptDisk(),
            [current],
            allowDestructiveReplace: true);

        Assert.Contains(diff, entry => entry.Action == "Clear" && entry.RiskLevel == "Destructive");
        Assert.Contains(diff, entry => entry.DiskpartScript.Contains("clean", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(diff, entry => entry.Action == "Create");
    }

    private static PartitionLayoutSpec ValidSpec() => new()
    {
        Style = "GPT",
        Partitions =
        [
            new PartitionSpec
            {
                SizeMB = "1024",
                FileSystem = "NTFS",
                Label = "Data",
                DriveLetter = "D"
            }
        ]
    };

    private static DiskInfo RawDisk() => new()
    {
        Number = 3,
        FriendlyName = "Test Disk",
        PartitionStyle = "RAW",
        Size = 64L * 1024 * 1024 * 1024
    };

    private static DiskInfo GptDisk() => new()
    {
        Number = 3,
        FriendlyName = "Test Disk",
        PartitionStyle = "GPT",
        Size = 64L * 1024 * 1024 * 1024
    };

    private static PartitionInfo MatchingPartition() => new()
    {
        DiskNumber = 3,
        PartitionNumber = 1,
        DriveLetter = 'D',
        Label = "Data",
        FileSystem = "NTFS",
        Size = 1024L * 1024L * 1024L,
        Type = "Basic"
    };
}
