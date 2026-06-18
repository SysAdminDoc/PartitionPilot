namespace PartitionPilot.Tests;

public class DiskCloningViewModelTests
{
    private const long GiB = 1024L * 1024L * 1024L;

    [Theory]
    [InlineData("e", 'E')]
    [InlineData(" Z ", 'Z')]
    public void RequireDriveLetter_ReturnsUppercaseLetter(string value, char expected)
    {
        Assert.Equal(expected, DiskCloningViewModel.RequireDriveLetter(value, "test volume"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("AB")]
    [InlineData("1")]
    public void RequireDriveLetter_ThrowsWhenLetterIsMissing(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => DiskCloningViewModel.RequireDriveLetter(value, "test volume"));

        Assert.Contains("test volume", ex.Message);
    }

    [Fact]
    public void EstimateImageBytes_UsesUsedSpacePlusOverhead()
    {
        var estimated = DiskCloningViewModel.EstimateImageBytes(10 * GiB, 4 * GiB);

        Assert.Equal((6 * GiB) + (512L * 1024L * 1024L), estimated);
    }

    [Fact]
    public void EstimateImageBytes_UsesMinimumForMostlyEmptyVolumes()
    {
        var estimated = DiskCloningViewModel.EstimateImageBytes(10 * GiB, 10 * GiB);

        Assert.Equal(GiB + (512L * 1024L * 1024L), estimated);
    }

    [Fact]
    public void PreflightImageDestination_AcceptsSafeDestination()
    {
        var result = DiskCloningViewModel.PreflightImageDestination(
            @"D:\Images\capture.vhdx",
            'C',
            6 * GiB,
            KnownDirectoryExists,
            _ => false,
            _ => 12 * GiB);

        Assert.Equal(@"D:\Images\capture.vhdx", result.FullPath);
        Assert.Equal(@"D:\", result.DestinationRoot);
        Assert.Equal(6 * GiB, result.EstimatedRequiredBytes);
        Assert.Equal(12 * GiB, result.DestinationFreeBytes);
    }

    [Fact]
    public void PreflightImageDestination_RejectsSelfReferentialDestination()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DiskCloningViewModel.PreflightImageDestination(
                @"C:\Images\capture.wim",
                'C',
                GiB,
                KnownDirectoryExists,
                _ => false,
                _ => 12 * GiB));

        Assert.Contains("outside the source volume", ex.Message);
    }

    [Fact]
    public void PreflightImageDestination_RejectsMissingDestinationFolder()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DiskCloningViewModel.PreflightImageDestination(
                @"D:\Missing\capture.vhdx",
                'C',
                GiB,
                KnownDirectoryExists,
                _ => false,
                _ => 12 * GiB));

        Assert.Contains("Create the destination folder", ex.Message);
    }

    [Fact]
    public void PreflightImageDestination_RejectsExistingImageFile()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DiskCloningViewModel.PreflightImageDestination(
                @"D:\Images\capture.vhdx",
                'C',
                GiB,
                KnownDirectoryExists,
                _ => true,
                _ => 12 * GiB));

        Assert.Contains("new image path", ex.Message);
    }

    [Fact]
    public void PreflightImageDestination_RejectsInsufficientFreeSpace()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DiskCloningViewModel.PreflightImageDestination(
                @"D:\Images\capture.vhdx",
                'C',
                8 * GiB,
                KnownDirectoryExists,
                _ => false,
                _ => 4 * GiB));

        Assert.Contains("may require", ex.Message);
    }

    [Fact]
    public void PreflightImageDestination_RejectsUnsupportedExtensions()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DiskCloningViewModel.PreflightImageDestination(
                @"D:\Images\capture.iso",
                'C',
                GiB,
                KnownDirectoryExists,
                _ => false,
                _ => 12 * GiB));

        Assert.Contains(".wim or .vhdx", ex.Message);
    }

    private static bool KnownDirectoryExists(string path)
    {
        return string.Equals(path, @"C:\", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(path, @"C:\Images", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(path, @"D:\", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(path, @"D:\Images", StringComparison.OrdinalIgnoreCase);
    }
}
