using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace PartitionPilot;

[MarkupExtensionReturnType(typeof(string))]
public class LocExtension : MarkupExtension
{
    /// <summary>
    /// The culture used for lookups. Kept as a passthrough to <see cref="LocalizationSource"/> so the
    /// markup extension and the binding source can never disagree about which language is active.
    /// </summary>
    public static CultureInfo? Culture
    {
        get => LocalizationSource.Instance.Culture;
        set => LocalizationSource.Instance.SetCulture(value);
    }

    public string Key { get; set; } = "";

    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
            return "";

        var target = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;

        // Inside a template the target is not known yet; returning the extension makes WPF ask again
        // once there is a real element to bind to.
        if (target?.TargetObject is null)
            return this;

        // Anything that is not a dependency property cannot carry a binding, so it gets a fixed string
        // and simply will not follow a later language change.
        if (target.TargetObject is not DependencyObject || target.TargetProperty is not DependencyProperty)
            return LocalizationSource.Resolve(Key);

        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationSource.Instance,
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }

    public static string Get(string key) => LocalizationSource.Resolve(key);
}
