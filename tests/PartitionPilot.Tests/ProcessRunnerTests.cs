namespace PartitionPilot.Tests;

public class ProcessRunnerTests
{
    [Theory]
    [InlineData("MyVolume", "MyVolume")]
    [InlineData("My\"Volume", "MyVolume")]
    [InlineData("Line\r\nBreak", "LineBreak")]
    [InlineData("", "")]
    [InlineData("test;rm -rf", "testrm -rf")]
    [InlineData("a&b|c$d`e(f)g", "abcdefg")]
    public void SanitizeLabel_RemovesDangerousChars(string input, string expected)
    {
        Assert.Equal(expected, ProcessRunner.SanitizeLabel(input));
    }

    [Theory]
    [InlineData("simple", "'simple'")]
    [InlineData("it's", "'it''s'")]
    [InlineData("a'b'c", "'a''b''c'")]
    [InlineData("", "''")]
    [InlineData("C:\\Users\\test", "'C:\\Users\\test'")]
    [InlineData("path with spaces", "'path with spaces'")]
    [InlineData("inject'; Remove-Item C:\\ -Recurse; '", "'inject''; Remove-Item C:\\ -Recurse; '''")]
    public void EscapePowerShellString_WrapsInSingleQuotes(string input, string expected)
    {
        Assert.Equal(expected, ProcessRunner.EscapePowerShellString(input));
    }

    [Theory]
    [InlineData(@"C:\Images\capture.wim")]
    [InlineData(@"D:\Folder With Spaces\disk.vhdx")]
    public void ValidateNativePathArgument_AllowsNormalPaths(string path)
    {
        Assert.Equal(path, ProcessRunner.ValidateNativePathArgument(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\bad\"path.vhdx")]
    [InlineData("C:\\bad\r\nclean.vhdx")]
    [InlineData("C:\\bad\0path.vhdx")]
    public void ValidateNativePathArgument_RejectsUnsafeNativeToolPaths(string path)
    {
        Assert.Throws<ArgumentException>(() => ProcessRunner.ValidateNativePathArgument(path));
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

    [Theory]
    [InlineData("NTFS")]
    [InlineData("FAT32")]
    [InlineData("exFAT")]
    [InlineData("ReFS")]
    [InlineData("ntfs")]
    public void ValidateFileSystem_AcceptsAllowed(string fs)
    {
        Assert.Equal(fs, ProcessRunner.ValidateFileSystem(fs));
    }

    [Theory]
    [InlineData("NTFS\nclean")]
    [InlineData("ext4")]
    [InlineData("")]
    public void ValidateFileSystem_RejectsInvalid(string fs)
    {
        Assert.Throws<ArgumentException>(() => ProcessRunner.ValidateFileSystem(fs));
    }
}
