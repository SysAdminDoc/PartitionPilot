using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Capturing;
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

    private static bool HasDesktopSession()
    {
        try
        {
            return Environment.UserInteractive &&
                   System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                       System.Runtime.InteropServices.OSPlatform.Windows);
        }
        catch { return false; }
    }

    private (Application app, Window window) LaunchSimulation()
    {
        var exePath = GetExePath();
        Assert.SkipWhen(!File.Exists(exePath), $"PartitionPilot.exe not found at {exePath}. Build first.");
        Assert.SkipWhen(!HasDesktopSession(), "No interactive desktop session available.");

        _automation = new UIA3Automation();
        _app = Application.Launch(exePath, "--simulate");
        var window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(20));
        Assert.NotNull(window);
        return (_app, window);
    }

    private void CaptureScreenshotOnFailure(Window? window, string testName)
    {
        if (window is null) return;
        try
        {
            var configuredDir = Environment.GetEnvironmentVariable("PARTITIONPILOT_UI_SCREENSHOT_DIR");
            var dir = string.IsNullOrWhiteSpace(configuredDir)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots")
                : configuredDir;
            Directory.CreateDirectory(dir);
            var image = Capture.Element(window);
            image.ToFile(Path.Combine(dir, $"{testName}_{DateTime.Now:yyyyMMdd_HHmmss}.png"));
        }
        catch { }
    }

    [Fact]
    public void App_Launches_InSimulationMode()
    {
        var (app, window) = LaunchSimulation();
        try
        {
            Assert.Contains("PartitionPilot", window.Title);
        }
        catch
        {
            CaptureScreenshotOnFailure(window, nameof(App_Launches_InSimulationMode));
            throw;
        }
    }

    [Fact]
    public void MainWindow_Has_ExpectedTabs()
    {
        var (app, window) = LaunchSimulation();
        try
        {
            var tabs = window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.TabItem));
            Assert.True(tabs.Length >= 7, $"Expected at least 7 tabs, found {tabs.Length}");
        }
        catch
        {
            CaptureScreenshotOnFailure(window, nameof(MainWindow_Has_ExpectedTabs));
            throw;
        }
    }

    [Fact]
    public void RefreshButton_Has_AutomationName()
    {
        var (app, window) = LaunchSimulation();
        try
        {
            var refreshBtn = window.FindFirstDescendant(cf => cf.ByName("Refresh current workspace"));
            Assert.NotNull(refreshBtn);
        }
        catch
        {
            CaptureScreenshotOnFailure(window, nameof(RefreshButton_Has_AutomationName));
            throw;
        }
    }

    [Fact]
    public void ActivityLog_Has_AutomationName()
    {
        var (app, window) = LaunchSimulation();
        try
        {
            var logList = window.FindFirstDescendant(cf => cf.ByName("Activity log entries"));
            Assert.NotNull(logList);
        }
        catch
        {
            CaptureScreenshotOnFailure(window, nameof(ActivityLog_Has_AutomationName));
            throw;
        }
    }

    [Fact]
    public void ThemeToggle_Has_AutomationName()
    {
        var (app, window) = LaunchSimulation();
        try
        {
            var themeBtn = window.FindFirstDescendant(cf => cf.ByName("Toggle theme"));
            Assert.NotNull(themeBtn);
        }
        catch
        {
            CaptureScreenshotOnFailure(window, nameof(ThemeToggle_Has_AutomationName));
            throw;
        }
    }

    public void Dispose()
    {
        _app?.Close();
        _automation?.Dispose();
    }
}
