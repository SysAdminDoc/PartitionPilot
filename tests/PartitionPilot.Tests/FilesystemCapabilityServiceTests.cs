namespace PartitionPilot.Tests;

public class FilesystemCapabilityServiceTests
{
    [Theory]
    [InlineData("NTFS", true, true, true, true, true, true)]
    [InlineData("FAT32", true, true, false, false, true, true)]
    [InlineData("exFAT", true, true, false, false, false, true)]
    [InlineData("ReFS", true, true, false, true, true, true)]
    [InlineData("FAT16", false, true, false, false, true, true)]
    [InlineData("ext4", false, false, false, false, false, false)]
    [InlineData("APFS", false, false, false, false, false, false)]
    [InlineData("HFS+", false, false, false, false, false, false)]
    [InlineData("Linux Swap", false, false, false, false, false, false)]
    [InlineData("LUKS", false, false, false, false, false, false)]
    public void Evaluate_ReturnsExpectedOperationMatrix(
        string fileSystem,
        bool canCreate,
        bool canFormat,
        bool canResize,
        bool canExtend,
        bool canCheck,
        bool canLabel)
    {
        Assert.Equal(canCreate, FilesystemCapabilityService.Evaluate(fileSystem, FilesystemOperation.Create).IsAllowed);
        Assert.Equal(canFormat, FilesystemCapabilityService.Evaluate(fileSystem, FilesystemOperation.Format).IsAllowed);
        Assert.Equal(canResize, FilesystemCapabilityService.Evaluate(fileSystem, FilesystemOperation.Resize).IsAllowed);
        Assert.Equal(canExtend, FilesystemCapabilityService.Evaluate(fileSystem, FilesystemOperation.Extend).IsAllowed);
        Assert.Equal(canCheck, FilesystemCapabilityService.Evaluate(fileSystem, FilesystemOperation.Check).IsAllowed);
        Assert.Equal(canLabel, FilesystemCapabilityService.Evaluate(fileSystem, FilesystemOperation.Label).IsAllowed);
    }

    [Theory]
    [InlineData("FAT", "FAT16")]
    [InlineData("ext2", "ext2/3/4")]
    [InlineData("ext3", "ext2/3/4")]
    [InlineData("Linux Root (x86-64)", "ext2/3/4")]
    public void Evaluate_UsesKnownAliases(string input, string expected)
    {
        var result = FilesystemCapabilityService.Evaluate(input, FilesystemOperation.Format);

        Assert.Equal(expected, result.FileSystem);
    }

    [Fact]
    public void Evaluate_UnknownFilesystemFailsClosedWithReason()
    {
        var result = FilesystemCapabilityService.Evaluate("XFS", FilesystemOperation.Check);

        Assert.False(result.IsAllowed);
        Assert.Equal("XFS", result.FileSystem);
        Assert.Contains("not in the supported Windows filesystem policy", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateFormatTarget_AllowsFormatOnlyLegacyFatButCreatePolicyRejectsIt()
    {
        Assert.Equal("FAT16", FilesystemCapabilityService.ValidateFormatTarget("FAT"));

        var create = FilesystemCapabilityService.Evaluate("FAT", FilesystemOperation.Create);
        Assert.False(create.IsAllowed);
        Assert.Contains("not supported", create.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
