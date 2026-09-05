using System.IO;
using System.Text.Json;

namespace PartitionPilot;

/// <summary>Shell layout the app remembers between runs.</summary>
public sealed class ShellSettings
{
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public int SelectedTabIndex { get; set; }
    public double? ActivityLogHeight { get; set; }
    public bool ActivityLogCollapsed { get; set; }
}

/// <summary>
/// Reads and writes the shell's remembered layout.
/// <para>
/// Kept separate from <see cref="ThemeService"/>'s single-value file so adding a setting does not risk
/// the theme preference, and stored as JSON so an unreadable or partial file degrades to defaults
/// instead of throwing during startup.
/// </para>
/// </summary>
public static class ShellSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsPath => Path.Combine(ResolveDirectory(), "shell.json");

    public static ShellSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
                return new ShellSettings();

            return JsonSerializer.Deserialize<ShellSettings>(File.ReadAllText(path)) ?? new ShellSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ShellSettings();
        }
    }

    public static void Save(ShellSettings settings, IActivityLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var directory = ResolveDirectory();
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "shell.json"), JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex)
        {
            log?.Log($"Window layout could not be saved: {ex.Message}");
        }
    }

    /// <summary>
    /// Clamps a remembered position back onto a display that still exists. A window restored onto a
    /// monitor that has since been unplugged would otherwise open off-screen with no way to reach it.
    /// </summary>
    public static bool TryGetVisiblePlacement(
        ShellSettings settings, System.Windows.Rect workArea,
        out double left, out double top, out double width, out double height)
    {
        left = top = width = height = 0;

        if (settings.WindowWidth is not { } storedWidth || settings.WindowHeight is not { } storedHeight)
            return false;
        if (storedWidth <= 0 || storedHeight <= 0)
            return false;
        if (settings.WindowLeft is not { } storedLeft || settings.WindowTop is not { } storedTop)
            return false;

        width = Math.Min(storedWidth, workArea.Width);
        height = Math.Min(storedHeight, workArea.Height);
        left = Math.Clamp(storedLeft, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        top = Math.Clamp(storedTop, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
        return true;
    }

    private static string ResolveDirectory()
    {
        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "portable.txt")))
            return exeDir;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PartitionPilot");
    }
}
