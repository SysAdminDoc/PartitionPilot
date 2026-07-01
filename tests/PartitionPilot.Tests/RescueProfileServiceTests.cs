using System.Text.Json;

namespace PartitionPilot.Tests;

public class RescueProfileServiceTests
{
    [Fact]
    public async Task CreateAsync_WritesPortableLaunchersNotesAndManifest()
    {
        var root = CreateTempDir();
        try
        {
            var source = Path.Combine(root, "publish");
            var output = Path.Combine(root, "rescue");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "pp.exe"), "cli");
            File.WriteAllText(Path.Combine(source, "PartitionPilot.exe"), "gui");
            Directory.CreateDirectory(Path.Combine(source, "runtimes"));
            File.WriteAllText(Path.Combine(source, "runtimes", "native.dll"), "native");

            var result = await RescueProfileService.CreateAsync(
                source,
                output,
                "1.2.3",
                TestContext.Current.CancellationToken);

            Assert.True(File.Exists(Path.Combine(output, "pp.exe")));
            Assert.True(File.Exists(Path.Combine(output, "PartitionPilot.exe")));
            Assert.True(File.Exists(Path.Combine(output, "runtimes", "native.dll")));
            Assert.True(File.Exists(Path.Combine(output, RescueProfileService.DiagnosticsLauncherName)));
            Assert.True(File.Exists(Path.Combine(output, RescueProfileService.GuiLauncherName)));
            Assert.True(File.Exists(result.NotesPath));

            var launcher = File.ReadAllText(Path.Combine(output, RescueProfileService.DiagnosticsLauncherName));
            Assert.Contains("diagnostics --rescue", launcher);

            var manifest = JsonSerializer.Deserialize<RescueProfileManifest>(
                File.ReadAllText(result.ManifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(manifest);
            Assert.Equal("1.2.3", manifest!.AppVersion);
            Assert.True(manifest.IncludesGui);
            Assert.Contains("pp.exe", manifest.CopiedFiles);
            Assert.Contains(manifest.SourceChecks, c => c.Name == "PartitionPilot CLI" && c.Status == "OK");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ValidateSourceDirectory_ReportsMissingCliAsError()
    {
        var root = CreateTempDir();
        try
        {
            var checks = RescueProfileService.ValidateSourceDirectory(root);

            Assert.Contains(checks, c => c.Name == "Source Directory" && c.Status == "OK");
            Assert.Contains(checks, c => c.Name == "PartitionPilot CLI" && c.Status == "Error");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CheckWinPeRuntime_UsesSystemDriveOrMiniNtProbe()
    {
        var fullWindows = EnvironmentDiagnostics.CheckWinPeRuntime(_ => "C:", () => false);
        var xDriveWinPe = EnvironmentDiagnostics.CheckWinPeRuntime(_ => "X:", () => false);
        var registryWinPe = EnvironmentDiagnostics.CheckWinPeRuntime(_ => "C:", () => true);

        Assert.Equal("Info", fullWindows.Status);
        Assert.Equal("OK", xDriveWinPe.Status);
        Assert.Equal("OK", registryWinPe.Status);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pp-rescue-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
