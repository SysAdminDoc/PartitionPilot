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
        ThemeService.LoadAndApply();
    }
}
