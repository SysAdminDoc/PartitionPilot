using System.Windows;
using Velopack;

namespace PartitionPilot;

public partial class App : Application
{
    public static bool IsSimulationMode { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        VelopackApp.Build().Run();
        base.OnStartup(e);

        IsSimulationMode = e.Args.Contains("--simulate", StringComparer.OrdinalIgnoreCase);

        // Before the first window is built, so the shell's XAML resolves in the right language rather
        // than rendering in English and switching a moment later.
        LanguageService.LoadAndApply(ShellSettingsService.Load().Language);
        ThemeService.LoadAndApply();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeService.Shutdown();
        base.OnExit(e);
    }
}
