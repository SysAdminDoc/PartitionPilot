using System.Globalization;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Markup;

namespace PartitionPilot.Tests;

/// <summary>
/// Covers the language selector end to end: that a switch reaches a live control without a restart,
/// that the choice round-trips through the settings file, and that an unconfigured install follows the
/// operating system.
/// </summary>
public class LanguageSwitchingTests : IDisposable
{
    private readonly AppLanguage _original = LanguageService.Current;

    [Fact]
    public void SwitchingLanguage_UpdatesAnAlreadyRenderedControl()
    {
        // The acceptance criterion this exists for: applying a language must reach the visible UI
        // without a restart. Before this change LocExtension resolved to a fixed string at XAML load,
        // so a control created in English stayed English for the life of the process.
        var (english, german) = RunOnStaThread(() =>
        {
            LanguageService.Apply(Language(""));

            var block = new TextBlock();
            var extension = new LocExtension("Refresh");
            var value = extension.ProvideValue(new TargetServiceProvider(block, TextBlock.TextProperty));

            // A binding, not a string: that is what makes the live update possible.
            Assert.IsType<System.Windows.Data.BindingExpressionBase>(value, exactMatch: false);
            block.SetValue(TextBlock.TextProperty, value);

            var before = block.Text;
            LanguageService.Apply(Language("de"));
            return (before, block.Text);
        });

        Assert.False(string.IsNullOrWhiteSpace(english));
        Assert.False(string.IsNullOrWhiteSpace(german));
        Assert.NotEqual(english, german);
    }

    [Fact]
    public void ProvideValue_FallsBackToAPlainStringForATargetThatCannotCarryABinding()
    {
        // Non-dependency-property targets exist in XAML; they must still get readable text rather than
        // a Binding object rendering as its type name.
        LanguageService.Apply(Language(""));

        var value = new LocExtension("Refresh").ProvideValue(new TargetServiceProvider(new object(), "NotADp"));

        Assert.IsType<string>(value);
        Assert.False(string.IsNullOrWhiteSpace((string)value));
    }

    [Fact]
    public void ProvideValue_DefersWhenTheTargetIsNotKnownYet()
    {
        // Inside a template WPF asks before there is an element; returning the extension makes it ask
        // again per instance.
        var extension = new LocExtension("Refresh");

        Assert.Same(extension, extension.ProvideValue(new TargetServiceProvider(null, null)));
    }

    [Fact]
    public void ProvideValue_ReturnsEmptyForAnEmptyKey()
    {
        Assert.Equal("", new LocExtension().ProvideValue(new TargetServiceProvider(null, null)));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("qps-ploc")]
    public void EveryShippedLanguage_ResolvesDifferentTextFromEnglish(string code)
    {
        LanguageService.Apply(Language(""));
        var english = LocExtension.Get("Refresh");

        LanguageService.Apply(Language(code));
        var translated = LocExtension.Get("Refresh");

        Assert.NotEqual(english, translated);
        Assert.False(string.IsNullOrWhiteSpace(translated));
    }

    [Fact]
    public void Apply_SetsTheCultureUsedForLookups()
    {
        LanguageService.Apply(Language("fr"));

        Assert.Equal("fr", LocalizationSource.Instance.Culture?.Name);
        Assert.Equal("fr", LanguageService.Current.Code);
    }

    [Fact]
    public void Apply_TreatsEnglishAsFollowingTheSystemRatherThanPinningACulture()
    {
        LanguageService.Apply(Language(""));

        Assert.Null(LocalizationSource.Instance.Culture);
    }

    [Fact]
    public void Apply_RaisesLanguageChangedSoImperativeLabelsCanRefresh()
    {
        var raised = 0;
        void Handler(object? s, EventArgs e) => raised++;

        LanguageService.LanguageChanged += Handler;
        try
        {
            LanguageService.Apply(Language("es"));
        }
        finally
        {
            LanguageService.LanguageChanged -= Handler;
        }

        Assert.Equal(1, raised);
    }

    [Fact]
    public void LoadAndApply_RestoresAStoredChoice()
    {
        LanguageService.LoadAndApply("fr");

        Assert.Equal("fr", LanguageService.Current.Code);
    }

    [Fact]
    public void LoadAndApply_FallsBackToEnglishForALanguageNoLongerShipped()
    {
        LanguageService.LoadAndApply("kl-GL");

        // Not an exception and not a broken UI: an unknown stored code behaves like no choice at all,
        // which on this machine resolves to English.
        Assert.Contains(LanguageService.Current, LanguageService.Available);
    }

    [Theory]
    [InlineData("de-AT", "de")]   // regional variants land on the base language
    [InlineData("de-CH", "de")]
    [InlineData("fr-CA", "fr")]
    [InlineData("es-MX", "es")]
    [InlineData("de", "de")]
    public void MatchCulture_ResolvesARegionalSystemLanguageToItsShippedBase(string osCulture, string expected)
    {
        Assert.Equal(expected, LanguageService.MatchCulture(CultureInfo.GetCultureInfo(osCulture))?.Code);
    }

    [Theory]
    [InlineData("ja-JP")]
    [InlineData("pt-BR")]
    [InlineData("")]
    public void MatchCulture_ReturnsNothingForALanguageTheAppDoesNotShip(string osCulture)
    {
        // Null means "no match", and the caller then uses English rather than showing resource keys.
        Assert.Null(LanguageService.MatchCulture(CultureInfo.GetCultureInfo(osCulture)));
    }

    [Fact]
    public void Resolve_IgnoresCaseSoAStoredCodeSurvivesCasingDifferences()
    {
        Assert.Equal("qps-ploc", LanguageService.Resolve("QPS-PLOC")?.Code);
        Assert.Null(LanguageService.Resolve(null));
    }

    [Fact]
    public void Available_ListsEnglishFirstAndNamesEachLanguageInItself()
    {
        // A user stranded in a language they cannot read has to be able to find their own.
        Assert.Equal("", LanguageService.Available[0].Code);
        Assert.Equal("English", LanguageService.Available[0].DisplayName);
        Assert.Contains(LanguageService.Available, l => l.DisplayName == "Deutsch");
        Assert.Contains(LanguageService.Available, l => l.DisplayName == "Français");
        Assert.All(LanguageService.Available, l => Assert.False(string.IsNullOrWhiteSpace(l.DisplayName)));
    }

    [Fact]
    public void ShellSettings_RoundTripsTheChosenLanguage()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new ShellSettings { Language = "de" });
        var restored = System.Text.Json.JsonSerializer.Deserialize<ShellSettings>(json);

        Assert.Equal("de", restored!.Language);
    }

    [Fact]
    public void ShellSettings_LeavesLanguageNullWhenNothingWasEverChosen()
    {
        // Null is meaningfully different from "": it means follow the operating system.
        Assert.Null(new ShellSettings().Language);
    }

    private static AppLanguage Language(string code) =>
        LanguageService.Resolve(code) ?? throw new InvalidOperationException($"'{code}' is not a shipped language.");

    /// <summary>WPF elements have thread affinity and require an STA apartment.</summary>
    private static T RunOnStaThread<T>(Func<T> action)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { failure = ex; }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            throw new InvalidOperationException("STA thread failed.", failure);

        return result;
    }

    public void Dispose()
    {
        LanguageService.Apply(_original);
        GC.SuppressFinalize(this);
    }

    /// <summary>Stands in for the service provider WPF hands a markup extension.</summary>
    private sealed class TargetServiceProvider(object? targetObject, object? targetProperty)
        : IServiceProvider, IProvideValueTarget
    {
        public object? TargetObject { get; } = targetObject;
        public object? TargetProperty { get; } = targetProperty;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IProvideValueTarget) ? this : null;
    }
}
