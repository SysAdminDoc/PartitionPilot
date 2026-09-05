namespace PartitionPilot.Tests;

public class DiskImageWorkflowServiceTests
{
    private const long GiB = 1024L * 1024L * 1024L;

    [Fact]
    public void PreflightDestination_RefusesToCaptureAVolumeIntoItself()
    {
        // Writing the image onto the volume being imaged grows the source while it is read, so the
        // capture chases its own output.
        var ex = Assert.Throws<InvalidOperationException>(() => Preflight(@"C:\images\c.wim", sourceDrive: 'C'));

        Assert.Contains("outside the source volume", ex.Message);
    }

    [Fact]
    public void PreflightDestination_RefusesWhenTheDestinationCannotHoldTheImage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Preflight(@"D:\images\c.wim", required: 200 * GiB, free: 10 * GiB));

        Assert.Contains("free", ex.Message);
    }

    [Fact]
    public void PreflightDestination_RefusesToOverwriteAnExistingImage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Preflight(@"D:\images\c.wim", fileExists: _ => true));

        Assert.Contains("delete the existing file", ex.Message);
    }

    [Theory]
    [InlineData(@"D:\images\c.img", "wim or .vhdx")]
    [InlineData(@"images\c.wim", "fully qualified")]
    [InlineData("", "destination path")]
    public void PreflightDestination_RefusesAPathItCannotUse(string path, string expectedFragment)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Preflight(path));

        Assert.Contains(expectedFragment, ex.Message);
    }

    [Fact]
    public void PreflightDestination_RefusesWhenNoSourceVolumeIsSelected()
    {
        Assert.Throws<InvalidOperationException>(() => Preflight(@"D:\images\c.wim", sourceDrive: default));
    }

    [Fact]
    public void PreflightDestination_RefusesWhenTheDestinationFolderDoesNotExist()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Preflight(@"D:\images\c.wim", directoryExists: p => p is @"D:\"));

        Assert.Contains("Create the destination folder", ex.Message);
    }

    [Fact]
    public void PreflightDestination_AcceptsAViableDestination()
    {
        var result = Preflight(@"D:\images\c.wim", required: 20 * GiB, free: 500 * GiB);

        Assert.Equal(@"D:\images\c.wim", result.FullPath);
        Assert.Equal(@"D:\", result.DestinationRoot);
        Assert.Equal(20 * GiB, result.EstimatedRequiredBytes);
        Assert.Equal(500 * GiB, result.DestinationFreeBytes);
    }

    [Fact]
    public void GuardSourceVolumeForCapture_RefusesALockedBitLockerVolume()
    {
        // Reading a locked volume yields ciphertext, so the image would restore as unusable bytes.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DiskImageWorkflowService.GuardSourceVolumeForCapture(
                'e', new Dictionary<char, string> { ['E'] = "Locked" }));

        Assert.Contains("E:", ex.Message);
    }

    [Fact]
    public void GuardSourceVolumeForCapture_AllowsAnUnlockedOrUnencryptedVolume()
    {
        DiskImageWorkflowService.GuardSourceVolumeForCapture(
            'e', new Dictionary<char, string> { ['E'] = "FullyDecrypted" });

        DiskImageWorkflowService.GuardSourceVolumeForCapture('e', new Dictionary<char, string>());
    }

    [Theory]
    [InlineData(10 * GiB, 4 * GiB)]  // 6 GiB used
    [InlineData(10 * GiB, -1)]       // unknown free space falls back to the whole volume
    [InlineData(10 * GiB, 99 * GiB)] // nonsense free space does the same
    public void EstimateImageBytes_NeverEstimatesBelowTheMinimumPlusOverhead(long size, long free)
    {
        Assert.True(DiskImageWorkflowService.EstimateImageBytes(size, free) >= GiB);
    }

    [Fact]
    public void EstimateImageBytes_ReturnsZeroForAnEmptySource()
    {
        Assert.Equal(0, DiskImageWorkflowService.EstimateImageBytes(0, 0));
    }

    private static ImageDestinationPreflight Preflight(
        string path,
        char sourceDrive = 'C',
        long required = 1 * GiB,
        long free = 100 * GiB,
        Func<string, bool>? directoryExists = null,
        Func<string, bool>? fileExists = null) =>
        DiskImageWorkflowService.PreflightDestination(
            path, sourceDrive, required,
            directoryExists ?? (_ => true),
            fileExists ?? (_ => false),
            _ => free);
}
