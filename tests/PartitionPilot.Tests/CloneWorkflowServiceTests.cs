namespace PartitionPilot.Tests;

public class CloneWorkflowServiceTests
{
    [Theory]
    [InlineData("/COPYALL")]   // security descriptors, owner and audit entries
    [InlineData("/DCOPY:DAT")] // directory timestamps
    [InlineData("/XJ")]        // do not walk junction points
    [InlineData("/B")]         // backup mode, for files whose ACLs would deny access
    [InlineData("/MIR")]
    public void BuildVhdxCaptureArguments_CarriesTheFidelitySwitches(string expectedSwitch)
    {
        var args = CloneWorkflowService.BuildVhdxCaptureArguments(@"\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7\", 'e');

        Assert.Contains($" {expectedSwitch} ", $" {args} ");
    }

    [Theory]
    [InlineData("System Volume Information")]
    [InlineData("$Recycle.Bin")]
    [InlineData("pagefile.sys")]
    [InlineData("hiberfil.sys")]
    [InlineData("swapfile.sys")]
    public void BuildVhdxCaptureArguments_ExcludesPerVolumeState(string excluded)
    {
        var args = CloneWorkflowService.BuildVhdxCaptureArguments(@"C:\", 'e');

        Assert.Contains($"\"{excluded}\"", args);
    }

    [Fact]
    public void BuildVhdxCaptureArguments_QuotesBothPathsAndNormalizesTheDestinationLetter()
    {
        var args = CloneWorkflowService.BuildVhdxCaptureArguments(@"\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7\", 'e');

        Assert.StartsWith(@"""\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7"" ""E:\""", args, StringComparison.Ordinal);
    }
}
