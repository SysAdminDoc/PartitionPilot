using System.Diagnostics;
using System.IO;

namespace PartitionPilot;

public class ProcessRunner
{
    public async Task<string> RunDiskpartAsync(string script, ActivityLog? log = null)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"pp_diskpart_{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(tempFile, script);
            log?.Log($"diskpart /s \"{tempFile}\"");
            return await RunExeAsync("diskpart", $"/s \"{tempFile}\"", log);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best-effort cleanup */ }
        }
    }

    public async Task<string> RunPowerShellAsync(string command, ActivityLog? log = null)
    {
        log?.Log($"powershell: {command}");
        return await RunExeAsync("powershell.exe",
            $"-NoProfile -NonInteractive -Command \"{command.Replace("\"", "\\\"")}\"", log);
    }

    public async Task<string> RunExeAsync(string fileName, string arguments, ActivityLog? log = null)
    {
        log?.Log($"Run: {fileName} {arguments}");

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        // Read stdout and stderr concurrently to avoid deadlocks
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
        {
            log?.Log($"ERROR ({process.ExitCode}): {stderr.Trim()}");
            throw new InvalidOperationException(
                $"{fileName} exited with code {process.ExitCode}: {stderr.Trim()}");
        }

        return stdout;
    }
}
