using System.IO;
using System.Windows;

namespace PartitionPilot;

public static class ThemeService
{
    private static readonly string SettingsDir = ResolveSettingsDir();
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.txt");

    private static string ResolveSettingsDir()
    {
        var exeDir = AppContext.BaseDirectory;
        var portableMarker = Path.Combine(exeDir, "portable.txt");
        if (File.Exists(portableMarker))
            return exeDir;
        // ProgramData is shared across elevation contexts (Windows 11 Administrator
        // Protection isolates AppData per elevation level). Use it if our settings
        // file already exists there; otherwise fall back to LocalAppData.
        var programData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PartitionPilot");
        var programDataSettings = Path.Combine(programData, "settings.txt");
        if (File.Exists(programDataSettings))
            return programData;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PartitionPilot");
    }

    public static bool IsDarkMode { get; private set; } = true;
    public static event EventHandler? ThemeChanged;

    public static void LoadAndApply()
    {
        IsDarkMode = LoadPreference();
        ApplyTheme(IsDarkMode);
    }

    public static void Toggle()
    {
        IsDarkMode = !IsDarkMode;
        SavePreference(IsDarkMode);
        ApplyTheme(IsDarkMode);
    }

    private static void ApplyTheme(bool dark)
    {
        var themeUri = new Uri(dark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml", UriKind.Relative);
        var themeDict = new ResourceDictionary { Source = themeUri };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0)
            merged[0] = themeDict;
        else
            merged.Insert(0, themeDict);

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool LoadPreference()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var content = File.ReadAllText(SettingsFile).Trim();
                return !content.Equals("light", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { /* default to dark */ }
        return true;
    }

    private static void SavePreference(bool dark)
    {
        var value = dark ? "dark" : "light";
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsFile, value);
        }
        catch { /* best-effort */ }

        // Also write to ProgramData so settings persist across elevation contexts
        try
        {
            var sharedDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PartitionPilot");
            Directory.CreateDirectory(sharedDir);
            File.WriteAllText(Path.Combine(sharedDir, "settings.txt"), value);
        }
        catch { /* ProgramData write may fail when non-elevated — expected */ }
    }
}
