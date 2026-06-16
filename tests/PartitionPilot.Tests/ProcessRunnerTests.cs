namespace PartitionPilot.Tests;

public class ProcessRunnerTests
{
    [Theory]
    [InlineData("MyVolume", "MyVolume")]
    [InlineData("My\"Volume", "MyVolume")]
    [InlineData("Line\r\nBreak", "LineBreak")]
    [InlineData("", "")]
    public void SanitizeLabel_RemovesDangerousChars(string input, string expected)
    {
        Assert.Equal(expected, ProcessRunner.SanitizeLabel(input));
    }

    [Theory]
    [InlineData('c', 'C')]
    [InlineData('D', 'D')]
    [InlineData('z', 'Z')]
    public void ValidateDriveLetter_NormalizesToUpper(char input, char expected)
    {
        Assert.Equal(expected, ProcessRunner.ValidateDriveLetter(input));
    }

    [Theory]
    [InlineData('1')]
    [InlineData('!')]
    [InlineData('\0')]
    public void ValidateDriveLetter_ThrowsOnInvalid(char input)
    {
        Assert.Throws<ArgumentException>(() => ProcessRunner.ValidateDriveLetter(input));
    }
}
