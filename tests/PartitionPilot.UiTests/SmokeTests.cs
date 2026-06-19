using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.UIA3;

namespace PartitionPilot.UiTests;

public class SmokeTests : IDisposable
{
    private Application? _app;
    private UIA3Automation? _automation;

    private static string GetExePath()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        var root = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "..", ".."));
        var exePath = Path.Combine(root, "src", "PartitionPilot", "bin", "Release", "net10.0-windows", "win-x64", "PartitionPilot.exe");
        if (!File.Exists(exePath))
            exePath = Path.Combine(root, "src", "PartitionPilot", "bin", "Debug", "net10.0-windows", "win-x64", "PartitionPilot.exe");
        return exePath;
    }

    [Fact(Skip = "Requires built exe and desktop session — run manually")]
    public void App_Launches_InSimulationMode()
    {
        var exePath = GetExePath();
        if (!File.Exists(exePath))
        {
            Assert.Fail($"PartitionPilot.exe not found at {exePath}. Build first.");
            return;
        }

        _automation = new UIA3Automation();
        _app = Application.Launch(exePath, "--simulate");
        var window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);
        Assert.Contains("PartitionPilot", window.Title);
    }

    [Fact(Skip = "Requires built exe and desktop session — run manually")]
    public void MainWindow_Has_ExpectedTabs()
    {
        var exePath = GetExePath();
        if (!File.Exists(exePath)) return;

        _automation = new UIA3Automation();
        _app = Application.Launch(exePath, "--simulate");
        var window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);

        var tabs = window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.TabItem));
        Assert.True(tabs.Length >= 7, $"Expected at least 7 tabs, found {tabs.Length}");
    }

    [Fact(Skip = "Requires built exe and desktop session — run manually")]
    public void RefreshButton_Has_AutomationName()
    {
        var exePath = GetExePath();
        if (!File.Exists(exePath)) return;

        _automation = new UIA3Automation();
        _app = Application.Launch(exePath, "--simulate");
        var window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(15));
        Assert.NotNull(window);

        var refreshBtn = window.FindFirstDescendant(cf => cf.ByName("Refresh current workspace"));
        Assert.NotNull(refreshBtn);
    }

    public void Dispose()
    {
        _app?.Close();
        _automation?.Dispose();
    }
}
