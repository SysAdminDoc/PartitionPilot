namespace PartitionPilot.Tests;

public class DiskImageManifestServiceTests
{
    [Fact]
    public async Task CreateManifestAsync_WritesImageHashAndSourceStats()
    {
        var dir = CreateTempDir();
        try
        {
            var source = Path.Combine(dir, "source");
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(Path.Combine(source, "file-a.txt"), "alpha");
            await File.WriteAllTextAsync(Path.Combine(source, "file-b.txt"), "bravo");

            var image = Path.Combine(dir, "capture.vhdx");
            await File.WriteAllTextAsync(image, "image");
            var volume = new VolumeInfo
            {
                DriveLetter = 'T',
                FileSystemLabel = "Source",
                FileSystemType = "NTFS",
                Size = 1000,
                SizeRemaining = 250
            };

            var manifest = await DiskImageManifestService.CreateManifestAsync(
                image, 'T', source, volume, "0.9.17");

            Assert.Equal("capture.vhdx", manifest.ImageFileName);
            Assert.Equal("T:", manifest.SourceDrive);
            Assert.Equal("NTFS", manifest.SourceFileSystem);
            Assert.Equal(2, manifest.SourceFileCount);
            Assert.Equal(10, manifest.SourceTotalBytes);
            Assert.Equal(2, manifest.SampleHashes.Count);
            Assert.True(File.Exists(DiskImageManifestService.GetManifestPath(image)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateManifestAsync_DetectsImageHashMismatch()
    {
        var dir = CreateTempDir();
        try
        {
            var source = Path.Combine(dir, "source");
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(Path.Combine(source, "file.txt"), "alpha");

            var image = Path.Combine(dir, "capture.wim");
            await File.WriteAllTextAsync(image, "image");
            await DiskImageManifestService.CreateManifestAsync(image, 'T', source, null, "0.9.17");

            await File.WriteAllTextAsync(image, "tampered");
            var validation = await DiskImageManifestService.ValidateManifestAsync(image);

            Assert.False(validation.IsValid);
            Assert.Equal("HashMismatch", validation.Status);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RebindManifestToEncryptedImageAsync_PreservesPlainHashAndValidatesEncryptedFile()
    {
        var dir = CreateTempDir();
        try
        {
            var source = Path.Combine(dir, "source");
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(Path.Combine(source, "file.txt"), "alpha");

            var image = Path.Combine(dir, "capture.vhdx");
            await File.WriteAllTextAsync(image, "plain image");
            var manifest = await DiskImageManifestService.CreateManifestAsync(image, 'T', source, null, "0.9.17");
            var plainHash = manifest.ImageSha256;

            var encrypted = image + ".enc";
            await File.WriteAllTextAsync(encrypted, "encrypted image");
            var rebound = await DiskImageManifestService.RebindManifestToEncryptedImageAsync(manifest, encrypted);

            Assert.True(rebound.IsEncrypted);
            Assert.Equal(plainHash, rebound.PlainImageSha256);
            Assert.Equal("capture.vhdx.enc", rebound.ImageFileName);

            var validation = await DiskImageManifestService.ValidateManifestAsync(encrypted);
            Assert.True(validation.IsValid);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pp-image-manifest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
