using System.Text.Json;

namespace PartitionPilot;

public sealed class RescueProfileManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string AppVersion { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; }
    public string SourceDirectory { get; set; } = "";
    public string DiagnosticsCommand { get; set; } = "pp.exe diagnostics --rescue";
    public bool IncludesGui { get; set; }
    public List<string> CopiedFiles { get; set; } = new();
    public List<string> Launchers { get; set; } = new();
    public List<DiagnosticCheck> SourceChecks { get; set; } = new();
}

public sealed record RescueProfileResult(
    string OutputDirectory,
    string ManifestPath,
    string NotesPath,
    RescueProfileManifest Manifest);

public static class RescueProfileService
{
    public const string ManifestFileName = "rescue-profile.json";
    public const string NotesFileName = "RESCUE-NOTES.txt";
    public const string DiagnosticsLauncherName = "pp-rescue.cmd";
    public const string GuiLauncherName = "PartitionPilot-rescue.cmd";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static IReadOnlyList<DiagnosticCheck> ValidateSourceDirectory(string sourceDirectory)
    {
        var checks = new List<DiagnosticCheck>();
        var sourceFull = Path.GetFullPath(sourceDirectory);

        checks.Add(new DiagnosticCheck
        {
            Category = "Rescue Profile",
            Name = "Source Directory",
            Status = Directory.Exists(sourceFull) ? "OK" : "Error",
            Detail = Directory.Exists(sourceFull)
                ? $"{sourceFull} - available"
                : $"{sourceFull} - not found",
            Remediation = Directory.Exists(sourceFull)
                ? ""
                : "Publish PartitionPilot.Cli first, then pass that publish folder with --source"
        });

        var cliPath = Path.Combine(sourceFull, "pp.exe");
        checks.Add(new DiagnosticCheck
        {
            Category = "Rescue Profile",
            Name = "PartitionPilot CLI",
            Status = File.Exists(cliPath) ? "OK" : "Error",
            Detail = File.Exists(cliPath)
                ? "pp.exe found"
                : "pp.exe missing",
            Remediation = File.Exists(cliPath)
                ? ""
                : "Use the self-contained CLI publish folder or an installed PartitionPilot folder"
        });

        var guiPath = Path.Combine(sourceFull, "PartitionPilot.exe");
        checks.Add(new DiagnosticCheck
        {
            Category = "Rescue Profile",
            Name = "PartitionPilot GUI",
            Status = File.Exists(guiPath) ? "OK" : "Warning",
            Detail = File.Exists(guiPath)
                ? "PartitionPilot.exe found"
                : "PartitionPilot.exe missing; rescue profile will be CLI-only",
            Remediation = File.Exists(guiPath)
                ? ""
                : "Copy PartitionPilot.exe into the source folder if GUI launch support is required"
        });

        return checks;
    }

    public static async Task<RescueProfileResult> CreateAsync(
        string sourceDirectory,
        string outputDirectory,
        string appVersion,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
            throw new ArgumentException("Source directory is required.", nameof(sourceDirectory));
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

        var sourceFull = Path.GetFullPath(sourceDirectory);
        var outputFull = Path.GetFullPath(outputDirectory);
        ValidateOutputPath(sourceFull, outputFull);

        var checks = ValidateSourceDirectory(sourceFull).ToList();
        var errors = checks.Where(c => c.Status == "Error").ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException("Rescue profile source is not usable: " +
                                                string.Join("; ", errors.Select(e => e.Detail)));

        if (Directory.Exists(outputFull))
            Directory.Delete(outputFull, recursive: true);
        Directory.CreateDirectory(outputFull);

        var copiedFiles = new List<string>();
        foreach (var file in Directory.EnumerateFiles(sourceFull, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceFull, file);
            var destination = Path.Combine(outputFull, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var sourceStream = File.OpenRead(file);
            await using var destinationStream = File.Create(destination);
            await sourceStream.CopyToAsync(destinationStream, ct);
            copiedFiles.Add(relative);
        }

        var launchers = new List<string> { DiagnosticsLauncherName };
        await File.WriteAllTextAsync(
            Path.Combine(outputFull, DiagnosticsLauncherName),
            BuildDiagnosticsLauncher(),
            ct);

        var includesGui = File.Exists(Path.Combine(outputFull, "PartitionPilot.exe"));
        if (includesGui)
        {
            launchers.Add(GuiLauncherName);
            await File.WriteAllTextAsync(
                Path.Combine(outputFull, GuiLauncherName),
                BuildGuiLauncher(),
                ct);
        }

        var notesPath = Path.Combine(outputFull, NotesFileName);
        await File.WriteAllTextAsync(notesPath, BuildNotes(includesGui), ct);

        var manifest = new RescueProfileManifest
        {
            AppVersion = appVersion,
            GeneratedAt = DateTimeOffset.UtcNow,
            SourceDirectory = sourceFull,
            IncludesGui = includesGui,
            CopiedFiles = copiedFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(),
            Launchers = launchers,
            SourceChecks = checks
        };

        var manifestPath = Path.Combine(outputFull, ManifestFileName);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), ct);

        return new RescueProfileResult(outputFull, manifestPath, notesPath, manifest);
    }

    public static string BuildDiagnosticsLauncher() =>
        """
        @echo off
        setlocal
        cd /d "%~dp0"
        if not exist "%~dp0pp.exe" (
          echo Missing pp.exe in rescue folder.
          exit /b 2
        )
        echo PartitionPilot rescue diagnostics
        "%~dp0pp.exe" diagnostics --rescue
        exit /b %ERRORLEVEL%
        """;

    public static string BuildGuiLauncher() =>
        """
        @echo off
        setlocal
        cd /d "%~dp0"
        if not exist "%~dp0PartitionPilot.exe" (
          echo Missing PartitionPilot.exe in rescue folder.
          exit /b 2
        )
        if exist "%~dp0pp.exe" (
          "%~dp0pp.exe" diagnostics --rescue
          if errorlevel 1 echo Review rescue diagnostics before destructive disk work.
        )
        start "" "%~dp0PartitionPilot.exe"
        """;

    private static string BuildNotes(bool includesGui)
    {
        var guiLine = includesGui
            ? "PartitionPilot-rescue.cmd runs diagnostics and launches the GUI."
            : "This profile is CLI-only because PartitionPilot.exe was not present in the source folder.";

        return $"""
        PartitionPilot Rescue Profile

        1. Boot WinPE.
        2. Open a command prompt in this folder.
        3. Run pp-rescue.cmd.
        4. Fix any Error diagnostics before imaging, cloning, partitioning, or BitLocker work.

        {guiLine}

        Required WinPE capabilities:
        - WMI and StorageWMI optional components for MSFT_Disk/MSFT_Partition.
        - DiskPart for partition scripting.
        - DISM for WIM capture and apply.
        - BitLocker tooling and provider support for encrypted volumes.
        - BCDBoot for boot repair workflows.
        """;
    }

    private static void ValidateOutputPath(string sourceFull, string outputFull)
    {
        var root = Path.GetPathRoot(outputFull);
        if (string.IsNullOrWhiteSpace(root) ||
            string.Equals(TrimTrailingSeparator(root), TrimTrailingSeparator(outputFull), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output directory cannot be a drive root.", nameof(outputFull));

        if (PathsEqual(sourceFull, outputFull))
            throw new ArgumentException("Output directory cannot be the source directory.", nameof(outputFull));

        if (IsNestedPath(sourceFull, outputFull))
            throw new ArgumentException("Output directory cannot be inside the source directory.", nameof(outputFull));

        if (IsNestedPath(outputFull, sourceFull))
            throw new ArgumentException("Output directory cannot contain the source directory.", nameof(outputFull));
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(TrimTrailingSeparator(left), TrimTrailingSeparator(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsNestedPath(string parent, string child)
    {
        var normalizedParent = EnsureTrailingSeparator(Path.GetFullPath(parent));
        var normalizedChild = Path.GetFullPath(child);
        return normalizedChild.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;

    private static string TrimTrailingSeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
