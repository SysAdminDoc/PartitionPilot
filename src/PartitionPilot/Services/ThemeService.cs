using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace PartitionPilot;

public enum ThemePreference { Dark, Light, System }

public static class ThemeService
{
    private static readonly string SettingsDir = ResolveSettingsDir();
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.txt");

    public static ThemePreference Preference { get; private set; } = ThemePreference.Dark;
    public static bool IsDarkMode => ResolveIsDark();
    public static event EventHandler? ThemeChanged;

    public static void LoadAndApply()
    {
        Preference = LoadPreference();
        ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
    }

    public static void CycleTheme()
    {
        Preference = Preference switch
        {
            ThemePreference.Dark => ThemePreference.Light,
            ThemePreference.Light => ThemePreference.System,
            _ => ThemePreference.Dark
        };
        SavePreference(Preference);
        ApplyTheme();
    }

    public static string GetLabel() => Preference switch
    {
        ThemePreference.Dark => "Light Mode",
        ThemePreference.Light => "System Theme",
        _ => "Dark Mode"
    };

    private static void ApplyTheme()
    {
        var dark = ResolveIsDark();

        Application.Current.ThemeMode = Preference switch
        {
            ThemePreference.System => ThemeMode.System,
            ThemePreference.Light => ThemeMode.Light,
            _ => ThemeMode.Dark
        };

        var themeUri = new Uri(dark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml", UriKind.Relative);
        var themeDict = new ResourceDictionary { Source = themeUri };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0)
            merged[0] = themeDict;
        else
            merged.Insert(0, themeDict);

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool ResolveIsDark()
    {
        if (Preference == ThemePreference.Dark) return true;
        if (Preference == ThemePreference.Light) return false;
        return !IsSystemLightTheme();
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    private static void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (Preference != ThemePreference.System) return;
        Application.Current.Dispatcher.BeginInvoke(ApplyTheme);
    }

    private static ThemePreference LoadPreference()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var content = File.ReadAllText(SettingsFile).Trim().ToLowerInvariant();
                return content switch
                {
                    "light" => ThemePreference.Light,
                    "system" => ThemePreference.System,
                    _ => ThemePreference.Dark
                };
            }
        }
        catch { }
        return ThemePreference.Dark;
    }

    private static void SavePreference(ThemePreference preference)
    {
        var value = preference switch
        {
            ThemePreference.Light => "light",
            ThemePreference.System => "system",
            _ => "dark"
        };
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsFile, value);
        }
        catch { }

        try
        {
            var sharedDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PartitionPilot");
            Directory.CreateDirectory(sharedDir);
            File.WriteAllText(Path.Combine(sharedDir, "settings.txt"), value);
        }
        catch { }
    }

    private static string ResolveSettingsDir()
    {
        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "portable.txt")))
            return exeDir;
        var programData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PartitionPilot");
        if (File.Exists(Path.Combine(programData, "settings.txt")))
            return programData;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PartitionPilot");
    }
}
