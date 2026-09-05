using System.Text.Json;
using PartitionPilot.Cli;

namespace PartitionPilot.Tests;

public class CliErrorTests
{
    [Fact]
    public void ExitCodes_AreDistinctSoACallerCanBranchOnThem()
    {
        // The point of the change: every failure used to be 1, so a script could not tell a guard that
        // stopped before touching the disk from an operation that failed partway through it.
        int[] codes =
        [
            CliExit.Success, CliExit.OperationFailed, CliExit.CompletedWithWarnings, CliExit.UsageError,
            CliExit.TargetNotFound, CliExit.PreconditionFailed, CliExit.BlockedByGuard, CliExit.Cancelled
        ];

        Assert.Equal(codes.Length, codes.Distinct().Count());
    }

    [Fact]
    public void BlockedByGuard_IsNotTheSameCodeAsOperationFailed()
    {
        // After a clone or restore this is the difference between "nothing happened" and
        // "the disk may be half-written".
        Assert.NotEqual(CliExit.OperationFailed, CliExit.BlockedByGuard);
    }

    [Fact]
    public void Report_EmitsJsonOnTheFailurePathWhenJsonWasRequested()
    {
        var written = CaptureStdout(() =>
            new CliError("blocked_by_guard", "Restore blocked: identity mismatch.", "Recapture the snapshot.")
                .Report(json: true, CliExit.BlockedByGuard));

        using var document = JsonDocument.Parse(written);
        var error = document.RootElement.GetProperty("Error");

        Assert.Equal("blocked_by_guard", error.GetProperty("Code").GetString());
        Assert.Contains("identity mismatch", error.GetProperty("Message").GetString());
        Assert.Equal("Recapture the snapshot.", error.GetProperty("Remediation").GetString());
        Assert.Equal(CliExit.BlockedByGuard, document.RootElement.GetProperty("ExitCode").GetInt32());
    }

    [Fact]
    public void Report_WritesNothingToStdoutWhenJsonWasNotRequested()
    {
        // Plain-text failures belong on stderr so piped stdout stays parseable.
        var written = CaptureStdout(() =>
            new CliError("usage", "--disk N required.").Report(json: false, CliExit.UsageError));

        Assert.Equal("", written.Trim());
    }

    [Fact]
    public void Report_ReturnsTheExitCodeItWasGiven()
    {
        Assert.Equal(CliExit.TargetNotFound,
            new CliError("not_found", "Disk 99 not found.").Report(json: false, CliExit.TargetNotFound));
    }

    [Theory]
    [InlineData("usage", CliExit.UsageError)]
    [InlineData("not_found", CliExit.TargetNotFound)]
    [InlineData("precondition_failed", CliExit.PreconditionFailed)]
    [InlineData("blocked_by_guard", CliExit.BlockedByGuard)]
    [InlineData("cancelled", CliExit.Cancelled)]
    [InlineData("operation_failed", CliExit.OperationFailed)]
    public void Helpers_PairEachCodeWithItsExitStatus(string expectedCode, int expectedExit)
    {
        var actualExit = 0;
        var written = CaptureStdout(() => actualExit = expectedCode switch
        {
            "usage" => CliError.Usage(json: true, "m"),
            "not_found" => CliError.NotFound(json: true, "m"),
            "precondition_failed" => CliError.Precondition(json: true, "m"),
            "blocked_by_guard" => CliError.Blocked(json: true, "m"),
            "cancelled" => CliError.Cancelled(json: true, "m"),
            _ => CliError.Failed(json: true, "m")
        });

        using var document = JsonDocument.Parse(written);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("Error").GetProperty("Code").GetString());
        Assert.Equal(expectedExit, actualExit);
    }

    private static string CaptureStdout(Action action)
    {
        var original = Console.Out;
        using var buffer = new StringWriter();
        Console.SetOut(buffer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }

        return buffer.ToString();
    }
}
