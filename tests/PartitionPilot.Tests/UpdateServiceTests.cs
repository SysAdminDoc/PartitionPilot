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
        var result = Version.TryParse(latest, out var latestV) &&
                     Version.TryParse(current, out var currentV) &&
                     latestV > currentV;
        Assert.Equal(shouldBeNewer, result);
    }
}
