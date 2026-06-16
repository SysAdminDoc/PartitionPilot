namespace PartitionPilot;

public interface IProcessRunner
{
    Task<string> RunDiskpartAsync(string script, ActivityLog? log = null, CancellationToken ct = default);
    Task<string> RunPowerShellAsync(string command, ActivityLog? log = null, CancellationToken ct = default);
    Task<string> RunExeAsync(string fileName, string arguments, ActivityLog? log = null,
        bool ignoreStderrOnSuccess = false, CancellationToken ct = default);
}
