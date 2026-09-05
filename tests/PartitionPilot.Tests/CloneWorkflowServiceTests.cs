namespace PartitionPilot.Tests;

public class CloneWorkflowServiceTests
{
    private const string ShadowSource = @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7\";

    [Theory]
    [InlineData("/COPYALL")]   // security descriptors, owner and audit entries
    [InlineData("/DCOPY:DAT")] // directory timestamps
    [InlineData("/XJ")]        // do not walk junction points
    [InlineData("/B")]         // backup mode, for files whose ACLs would deny access
    [InlineData("/MIR")]
    public void BuildVhdxCaptureArguments_CarriesTheFidelitySwitches(string expectedSwitch)
    {
        var args = CloneWorkflowService.BuildVhdxCaptureArguments(ShadowSource, 'e');

        Assert.Contains($" {expectedSwitch} ", $" {args} ");
    }

    [Fact]
    public void BuildVhdxCaptureArguments_UnprivilegedFormStillCopiesAclsAndOwner()
    {
        // Robocopy exits 16 having copied nothing when /COPYALL or /B are asked for without the rights,
        // so the fallback drops only the audit entries and backup-mode reads.
        var args = CloneWorkflowService.BuildVhdxCaptureArguments(ShadowSource, 'e', privileged: false);

        Assert.Contains(" /COPY:DATSO ", $" {args} ");
        Assert.DoesNotContain(" /COPYALL ", $" {args} ");
        Assert.DoesNotContain(" /B ", $" {args} ");
        Assert.Contains(" /XJ ", $" {args} ");
        Assert.Contains(" /DCOPY:DAT ", $" {args} ");
    }

    [Theory]
    [InlineData("ERROR : You do not have the Manage Auditing user right.")]
    [InlineData("ERROR : You do not have the Backup and Restore Files user rights.")]
    public void IsMissingPrivilegeFailure_RecognizesTheRefusalsWorthRetrying(string message)
    {
        Assert.True(CloneWorkflowService.IsMissingPrivilegeFailure(message));
    }

    [Theory]
    [InlineData("ERROR 123 (0x0000007B) Accessing Destination Directory")]
    [InlineData("robocopy exited with code 8: some files could not be copied")]
    public void IsMissingPrivilegeFailure_DoesNotSwallowOtherFailures(string message)
    {
        Assert.False(CloneWorkflowService.IsMissingPrivilegeFailure(message));
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
        var args = CloneWorkflowService.BuildVhdxCaptureArguments(ShadowSource, 'e');

        // The destination keeps a doubled backslash on purpose: a quoted path ending in a single
        // backslash escapes its own closing quote, and robocopy then reads every following switch as
        // part of the destination path and fails with ERROR 123.
        Assert.StartsWith(@"""\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7"" ""E:\\""", args, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(ShadowSource)]
    [InlineData(@"D:\mount\point")]
    public void BuildVhdxCaptureArguments_KeepsQuotesBalancedSoEverySwitchStaysItsOwnArgument(string source)
    {
        var args = CloneWorkflowService.BuildVhdxCaptureArguments(source, 'e');

        Assert.False(HasUnbalancedQuotes(args),
            "Quoting is unbalanced, so robocopy would absorb the following switches into a path argument.");
    }

    /// <summary>
    /// Walks the argument string the way Windows parses one: a quote delimits an argument only when it
    /// is not preceded by an odd run of backslashes.
    /// </summary>
    private static bool HasUnbalancedQuotes(string args)
    {
        var inQuotes = false;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] != '"')
                continue;

            var backslashes = 0;
            for (var j = i - 1; j >= 0 && args[j] == '\\'; j--)
                backslashes++;

            if (backslashes % 2 == 0)
                inQuotes = !inQuotes;
        }

        return inQuotes;
    }
}
