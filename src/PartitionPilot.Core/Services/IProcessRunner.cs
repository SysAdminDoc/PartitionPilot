namespace PartitionPilot;

public interface IProcessRunner
{
    Task<string> RunDiskpartAsync(string script, IActivityLog? log = null, CancellationToken ct = default);
    Task<string> RunPowerShellAsync(string command, IActivityLog? log = null, CancellationToken ct = default);
    Task<string> RunExeAsync(string fileName, string arguments, IActivityLog? log = null,
        bool ignoreStderrOnSuccess = false, CancellationToken ct = default);
}
