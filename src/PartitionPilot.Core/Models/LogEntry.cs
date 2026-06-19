using System.Text.RegularExpressions;

namespace PartitionPilot;

public class LogEntry
{
    public string Timestamp { get; set; } = "";
    public string Level { get; set; } = "Info";
    public string Message { get; set; } = "";
}

public partial class OperationRecord
{
    private static int _nextId;

    public int OperationId { get; } = Interlocked.Increment(ref _nextId);
    public string CommandKind { get; init; } = "";
    public string TargetIdentifier { get; init; } = "";
    public string RedactedCommand { get; init; } = "";
    public int? ExitCode { get; set; }
    public long DurationMs { get; set; }
    public string CleanupStatus { get; set; } = "N/A";

    public string ToLogLine() =>
        $"[OP-{OperationId:D4}] {CommandKind} target={TargetIdentifier} exit={ExitCode?.ToString() ?? "?"} {DurationMs}ms cleanup={CleanupStatus}";

    public string ToRedactedLine() =>
        $"[OP-{OperationId:D4}] {CommandKind}: {RedactedCommand}";

    public static string RedactPaths(string command)
    {
        var result = PathPattern().Replace(command, "[path]");
        result = UserProfilePattern().Replace(result, "[user]");
        return result;
    }

    [GeneratedRegex("""[A-Za-z]:\\[^\s'"]+""")]
    private static partial Regex PathPattern();

    [GeneratedRegex("""C:\\Users\\[^\\]+""", RegexOptions.IgnoreCase)]
    private static partial Regex UserProfilePattern();
}
