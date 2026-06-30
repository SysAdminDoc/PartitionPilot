using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace PartitionPilot;

public class ProcessRunner : IProcessRunner
{
    private static readonly Regex DiskpartErrorPattern = new(
        @"\b(error|failed|cannot|unable to|not found|access is denied|the specified .+ does not exist)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<string> RunDiskpartAsync(string script, IActivityLog? log = null, CancellationToken ct = default)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"pp_diskpart_{Guid.NewGuid():N}.txt");
        var record = new OperationRecord
        {
            CommandKind = "diskpart",
            TargetIdentifier = "script",
            RedactedCommand = OperationRecord.RedactPaths($"diskpart /s \"{tempFile}\"")
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await File.WriteAllTextAsync(tempFile, script, ct);
            log?.Log($"diskpart /s \"{tempFile}\"");
            var output = await RunExeAsync("diskpart", $"/s \"{tempFile}\"", log, ct: ct);

            if (DiskpartErrorPattern.IsMatch(output))
            {
                record.ExitCode = -1;
                record.DurationMs = sw.ElapsedMilliseconds;
                log?.Log(record.ToLogLine());
                log?.Log($"Diskpart reported error in output: {output.Trim()}");
                throw new InvalidOperationException($"diskpart error: {output.Trim()}");
            }

            record.ExitCode = 0;
            record.DurationMs = sw.ElapsedMilliseconds;
            log?.Log(record.ToLogLine());
            return output;
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best-effort cleanup */ }
        }
    }

    public async Task<string> RunPowerShellAsync(string command, IActivityLog? log = null, CancellationToken ct = default)
    {
        var record = new OperationRecord
        {
            CommandKind = "powershell",
            TargetIdentifier = ExtractPowerShellTarget(command),
            RedactedCommand = OperationRecord.RedactPaths(command)
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        log?.Log($"powershell: {command}");
        var bytes = System.Text.Encoding.Unicode.GetBytes(command);
        var encoded = Convert.ToBase64String(bytes);
        try
        {
            var result = await RunExeAsync("powershell.exe",
                $"-NoProfile -NonInteractive -EncodedCommand {encoded}", log,
                ignoreStderrOnSuccess: true, ct: ct);
            record.ExitCode = 0;
            record.DurationMs = sw.ElapsedMilliseconds;
            log?.Log(record.ToLogLine());
            return result;
        }
        catch
        {
            record.ExitCode = -1;
            record.DurationMs = sw.ElapsedMilliseconds;
            log?.Log(record.ToLogLine());
            throw;
        }
    }

    private static string ExtractPowerShellTarget(string command)
    {
        if (command.Contains("-DriveLetter", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(command, @"-DriveLetter\s+'?([A-Z])'?", RegexOptions.IgnoreCase);
            if (match.Success) return $"Volume {match.Groups[1].Value}:";
        }
        if (command.Contains("-DiskNumber", StringComparison.OrdinalIgnoreCase) || command.Contains("-Number", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(command, @"-(?:DiskNumber|Number)\s+(\d+)", RegexOptions.IgnoreCase);
            if (match.Success) return $"Disk {match.Groups[1].Value}";
        }
        return "system";
    }

    public async Task<string> RunExeAsync(string fileName, string arguments, IActivityLog? log = null,
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

    public static string ValidateNativePathArgument(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        if (path.IndexOfAny(['"', '\r', '\n', '\0']) >= 0)
            throw new ArgumentException("Path contains unsupported characters for native disk tools.", nameof(path));

        return path;
    }

    public static char ValidateDriveLetter(char letter)
    {
        letter = char.ToUpperInvariant(letter);
        if (letter < 'A' || letter > 'Z')
            throw new ArgumentException($"Invalid drive letter: {letter}");
        return letter;
    }

    public static string ValidateFileSystem(string fs)
        => FilesystemCapabilityService.ValidateFormatTarget(fs);

    private static readonly HashSet<string> AllowedAllocationUnitSizes = new(StringComparer.Ordinal)
        { "512", "1024", "2048", "4096", "8192", "16384", "32768", "65536" };

    public static string? ValidateAllocationUnitSize(string? allocationUnitSize)
    {
        if (string.IsNullOrWhiteSpace(allocationUnitSize))
            return null;

        var normalized = allocationUnitSize.Trim();
        if (!AllowedAllocationUnitSizes.Contains(normalized))
            throw new ArgumentException($"Unsupported allocation unit size: {allocationUnitSize}");

        return normalized;
    }
}
