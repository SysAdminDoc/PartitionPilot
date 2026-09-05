using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace PartitionPilot;

/// <summary>
/// Binding source behind every <see cref="LocExtension"/>.
/// <para>
/// The markup extension used to resolve to a plain string at XAML load, which meant the roughly 570
/// localized labels in this app could only change language by restarting. Binding them to an indexer on
/// a single notifying object instead lets one <see cref="Refresh"/> re-evaluate all of them at once.
/// </para>
/// </summary>
public sealed class LocalizationSource : INotifyPropertyChanged
{
    /// <summary>WPF's convention for "every indexer value changed".</summary>
    private const string AllIndexersChanged = "Item[]";

    private static readonly ResourceManager Resources = new(
        "PartitionPilot.Properties.Strings",
        typeof(LocalizationSource).Assembly);

    public static LocalizationSource Instance { get; } = new();

    private LocalizationSource() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// The culture every lookup uses. Null follows the thread's UI culture, which is what an
    /// unconfigured install gets.
    /// </summary>
    public CultureInfo? Culture { get; private set; }

    public string this[string key] => Resolve(key);

    public static string Resolve(string key) =>
        string.IsNullOrEmpty(key) ? "" : Resources.GetString(key, Instance.Culture) ?? $"[{key}]";

    /// <summary>Switches language and re-evaluates every live binding.</summary>
    public void SetCulture(CultureInfo? culture)
    {
        Culture = culture;
        Refresh();
    }

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(AllIndexersChanged));
}
