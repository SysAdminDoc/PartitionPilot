using System.Text.Json;

namespace PartitionPilot.Cli;

/// <summary>
/// Exit codes a caller can branch on.
/// <para>
/// Everything used to return 1, so a script could not tell "a guard fail-closed and nothing was touched"
/// apart from "the operation failed partway through and the disk may be half-written" — a distinction
/// that matters a great deal after a clone or a restore.
/// </para>
/// </summary>
public static class CliExit
{
    public const int Success = 0;
    public const int OperationFailed = 1;
    public const int CompletedWithWarnings = 2;
    public const int UsageError = 3;
    public const int TargetNotFound = 4;
    public const int PreconditionFailed = 5;
    public const int BlockedByGuard = 6;
    public const int Cancelled = 7;
}

/// <summary>Machine-readable failure detail, so <c>--json</c> callers get JSON on the failure path too.</summary>
public sealed record CliError(string Code, string Message, string Remediation = "")
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Writes the failure and returns the exit code, honouring <paramref name="json"/>.</summary>
    public int Report(bool json, int exitCode)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Error = new { Code, Message, Remediation },
                ExitCode = exitCode
            }, JsonOptions));
        }
        else
        {
            Console.Error.WriteLine(Message);
            if (!string.IsNullOrWhiteSpace(Remediation))
                Console.Error.WriteLine(Remediation);
        }

        return exitCode;
    }

    public static int Usage(bool json, string message) =>
        new CliError("usage", message).Report(json, CliExit.UsageError);

    public static int NotFound(bool json, string message) =>
        new CliError("not_found", message).Report(json, CliExit.TargetNotFound);

    public static int Precondition(bool json, string message, string remediation = "") =>
        new CliError("precondition_failed", message, remediation).Report(json, CliExit.PreconditionFailed);

    public static int Blocked(bool json, string message, string remediation = "") =>
        new CliError("blocked_by_guard", message, remediation).Report(json, CliExit.BlockedByGuard);

    public static int Cancelled(bool json, string message = "Cancelled.") =>
        new CliError("cancelled", message).Report(json, CliExit.Cancelled);

    public static int Failed(bool json, string message) =>
        new CliError("operation_failed", message).Report(json, CliExit.OperationFailed);
}
