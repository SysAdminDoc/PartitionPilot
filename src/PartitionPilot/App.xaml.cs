using System.Windows;

namespace PartitionPilot;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeService.LoadAndApply();
    }
}
