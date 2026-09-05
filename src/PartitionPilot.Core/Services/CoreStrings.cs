using System.Globalization;
using System.Resources;

namespace PartitionPilot;

/// <summary>
/// Resource lookup for the text Core hands to a human: the confirmations that gate wiping, cloning and
/// overwriting a disk.
/// <para>
/// Core is a plain library shared by the WPF app and <c>pp.exe</c>, so it cannot reach the app's
/// <c>LocExtension</c>. Lookups follow <see cref="CultureInfo.CurrentUICulture"/> instead, which the app sets
/// when the user picks a language and which the CLI leaves at whatever the operating system reports. Both
/// therefore satisfy the lookup without Core knowing which one is calling.
/// </para>
/// </summary>
public static class CoreStrings
{
    private static readonly ResourceManager Resources =
        new("PartitionPilot.Properties.CoreStrings", typeof(CoreStrings).Assembly);

    /// <summary>
    /// Resolves a key, falling back to English. A missing key returns the key in brackets rather than an
    /// empty string, so a typo shows up as obviously wrong text instead of a blank confirmation.
    /// </summary>
    public static string Get(string key) =>
        string.IsNullOrEmpty(key) ? "" : Resources.GetString(key) ?? $"[{key}]";

    /// <summary>
    /// Resolves a key and substitutes runtime values. Falls back to the unformatted text when a translation's
    /// placeholders do not match the arguments: these strings gate destructive work, so a broken translation
    /// has to leave the warning readable rather than throw on the way to showing it.
    /// </summary>
    public static string Format(string key, params object?[] args)
    {
        var template = Get(key);

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
