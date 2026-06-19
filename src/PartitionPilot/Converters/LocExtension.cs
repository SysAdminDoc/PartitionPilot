using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace PartitionPilot;

[MarkupExtensionReturnType(typeof(string))]
public class LocExtension : MarkupExtension
{
    private static readonly ResourceManager Resources = new(
        "PartitionPilot.Properties.Strings",
        typeof(LocExtension).Assembly);

    public static CultureInfo? Culture { get; set; }

    public string Key { get; set; } = "";

    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key)) return "";
        return Resources.GetString(Key, Culture) ?? $"[{Key}]";
    }

    public static string Get(string key) =>
        Resources.GetString(key, Culture) ?? $"[{key}]";
}
