namespace PartitionPilot.Tests;

public class DiskSpdServiceTests
{
    [Fact]
    public void BuildProfileArguments_IncludesCreateFileArgumentAndKeepsTargetPathAtomic()
    {
        var profile = new DiskSpdService.DiskSpdProfile(
            "random read",
            "-b4K -o32 -t1 -r -W5 -d5 -Sh -w0 -Rxml");
        var targetPath = @"C:\Bench Target\pp diskspd.dat";

        var args = DiskSpdService.BuildProfileArguments(profile, targetPath);

        Assert.Equal("-c1G", args[0]);
        Assert.Contains("-Rxml", args);
        Assert.Equal(targetPath, args[^1]);
    }

    [Fact]
    public void BuildProfileArguments_RejectsMissingTargetPath()
    {
        var profile = DiskSpdService.StandardProfiles[0];

        Assert.Throws<ArgumentException>(() => DiskSpdService.BuildProfileArguments(profile, ""));
    }

    [Fact]
    public void ComputeSha256Hex_ReturnsUppercaseDigest()
    {
        var digest = DiskSpdService.ComputeSha256Hex([0x01, 0x02, 0x03]);

        Assert.Equal("039058C6F2C0CB492C533B0A4D14EF77CC0F78ABCCCED5287D84A1A2011CFB81", digest);
    }
}
