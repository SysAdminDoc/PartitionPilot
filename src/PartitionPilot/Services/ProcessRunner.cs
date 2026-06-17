using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace PartitionPilot;

public class ProcessRunner : IProcessRunner
{
    private static readonly Regex DiskpartErrorPattern = new(
        @"\b(error|failed|cannot|unable to|not found|access is denied|the specified .+ does not exist)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<string> RunDiskpartAsync(string script, ActivityLog? log = null, CancellationToken ct = default)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"pp_diskpart_{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(tempFile, script, ct);
            log?.Log($"diskpart /s \"{tempFile}\"");
            var output = await RunExeAsync("diskpart", $"/s \"{tempFile}\"", log, ct: ct);

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

    public async Task<string> RunPowerShellAsync(string command, ActivityLog? log = null, CancellationToken ct = default)
    {
        log?.Log($"powershell: {command}");
        var bytes = System.Text.Encoding.Unicode.GetBytes(command);
        var encoded = Convert.ToBase64String(bytes);
        return await RunExeAsync("powershell.exe",
            $"-NoProfile -NonInteractive -EncodedCommand {encoded}", log,
            ignoreStderrOnSuccess: true, ct: ct);
    }

    public async Task<string> RunExeAsync(string fileName, string arguments, ActivityLog? log = null,
        bool ignoreStderrOnSuccess = false, CancellationToken ct = default)
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

        await using var reg = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        var isRobocopy = fileName.Equals("robocopy", StringComparison.OrdinalIgnoreCase) ||
                         fileName.Equals("robocopy.exe", StringComparison.OrdinalIgnoreCase);
        var isFatalExit = isRobocopy ? process.ExitCode >= 8 : process.ExitCode != 0;

        if (isFatalExit)
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
        var sb = new System.Text.StringBuilder(label.Length);
        foreach (var c in label)
        {
            if (c is '"' or '\r' or '\n' or ';' or '&' or '|' or '$' or '`' or '(' or ')')
                continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static string EscapePowerShellString(string value)
    {
        return "'" + value.Replace("'", "''") + "'";
    }

    public static char ValidateDriveLetter(char letter)
    {
        letter = char.ToUpperInvariant(letter);
        if (letter < 'A' || letter > 'Z')
            throw new ArgumentException($"Invalid drive letter: {letter}");
        return letter;
    }

    private static readonly HashSet<string> AllowedFileSystems = new(StringComparer.OrdinalIgnoreCase)
        { "NTFS", "FAT32", "FAT", "exFAT", "ReFS" };

    public static string ValidateFileSystem(string fs)
    {
        if (!AllowedFileSystems.Contains(fs))
            throw new ArgumentException($"Unsupported file system: {fs}");
        return fs;
    }
}
