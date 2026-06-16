using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace PartitionPilot;

public class ProcessRunner
{
    private static readonly Regex DiskpartErrorPattern = new(
        @"\b(error|failed|cannot|unable to|not found|access is denied|the specified .+ does not exist)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<string> RunDiskpartAsync(string script, ActivityLog? log = null)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"pp_diskpart_{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(tempFile, script);
            log?.Log($"diskpart /s \"{tempFile}\"");
            var output = await RunExeAsync("diskpart", $"/s \"{tempFile}\"", log);

            if (DiskpartErrorPattern.IsMatch(output))
            {
                log?.Log($"Diskpart reported error in output: {output.Trim()}");
                throw new InvalidOperationException($"diskpart error: {output.Trim()}");
            }

            return output;
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
            $"-NoProfile -NonInteractive -Command \"{command.Replace("\"", "\\\"")}\"", log,
            ignoreStderrOnSuccess: true);
    }

    public async Task<string> RunExeAsync(string fileName, string arguments, ActivityLog? log = null,
        bool ignoreStderrOnSuccess = false)
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

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            var detail = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
            log?.Log($"ERROR ({process.ExitCode}): {detail}");
            throw new InvalidOperationException(
                $"{fileName} exited with code {process.ExitCode}: {detail}");
        }

        if (!ignoreStderrOnSuccess && !string.IsNullOrWhiteSpace(stderr))
            log?.Log($"WARN (stderr): {stderr.Trim()}");

        return stdout;
    }

    public static string SanitizeLabel(string label)
    {
        return label.Replace("\"", "").Replace("\r", "").Replace("\n", "");
    }

    public static char ValidateDriveLetter(char letter)
    {
        letter = char.ToUpperInvariant(letter);
        if (letter < 'A' || letter > 'Z')
            throw new ArgumentException($"Invalid drive letter: {letter}");
        return letter;
    }
}
