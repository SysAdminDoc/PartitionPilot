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
}
