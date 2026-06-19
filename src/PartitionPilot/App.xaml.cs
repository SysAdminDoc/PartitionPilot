using System.Windows;
using Velopack;

namespace PartitionPilot;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        VelopackApp.Build().Run();
        base.OnStartup(e);
        ThemeService.LoadAndApply();
    }
}
