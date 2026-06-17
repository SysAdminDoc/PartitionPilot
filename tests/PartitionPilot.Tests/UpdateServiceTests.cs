namespace PartitionPilot.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("0.2.0", "0.2.0", false)]
    [InlineData("0.1.0", "0.2.0", false)]
    [InlineData("0.3.0", "0.2.0", true)]
    [InlineData("0.10.0", "0.2.0", true)]
    [InlineData("1.0.0", "0.2.0", true)]
    public void VersionComparison_IsSemanticNotLexicographic(string latest, string current, bool shouldBeNewer)
    {
        Assert.Equal(shouldBeNewer, UpdateService.IsNewerVersion(latest, current));
    }

    [Fact]
    public void CurrentVersion_ComesFromAssemblyMetadata()
    {
        var current = UpdateService.GetCurrentVersion();

        Assert.NotEqual("0.2.3", current);
        Assert.True(Version.TryParse(current, out _));
        Assert.StartsWith("0.3.0", current);
    }
}
