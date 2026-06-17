namespace PartitionPilot.Tests;

public class DiskCloningViewModelTests
{
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
}
