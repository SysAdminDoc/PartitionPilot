namespace PartitionPilot.Tests;

public class ReleaseArtifactServiceTests
{
    [Fact]
    public async Task CreateManifestAsync_WritesSha256ManifestAndMarksUnsignedLocalTest()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pp-release-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var artifactPath = Path.Combine(dir, "PartitionPilot-0.9.10-Setup.exe");
            await File.WriteAllTextAsync(artifactPath, "release artifact");

            var manifest = await ReleaseArtifactService.CreateManifestAsync(dir, "0.9.10");

            Assert.True(manifest.IsLocalTestBuild);
            Assert.Equal("UnsignedLocalTest", manifest.SigningStatus);
            var artifact = Assert.Single(manifest.Artifacts);
            Assert.Equal("PartitionPilot-0.9.10-Setup.exe", artifact.FileName);
            Assert.Equal("UnsignedLocalTest", artifact.AuthenticodeStatus);
            Assert.True(File.Exists(Path.Combine(dir, "SHA256SUMS")));
            Assert.True(File.Exists(Path.Combine(dir, "SHA256SUMS.json")));

            var expected = await ReleaseArtifactService.ComputeSha256HexAsync(artifactPath);
            Assert.Equal(expected, artifact.Sha256);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyManifestAsync_ReportsHashMismatch()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pp-release-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var artifactPath = Path.Combine(dir, "PartitionPilot-0.9.10-Setup.exe");
            await File.WriteAllTextAsync(artifactPath, "release artifact");
            await ReleaseArtifactService.CreateManifestAsync(dir, "0.9.10");

            await File.WriteAllTextAsync(artifactPath, "tampered");

            var result = await ReleaseArtifactService.VerifyManifestAsync(dir);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("SHA256 mismatch", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
