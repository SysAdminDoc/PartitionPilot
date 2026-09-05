using System.Globalization;

namespace PartitionPilot;

/// <summary>A language the app ships translations for.</summary>
/// <param name="Code">Culture name, or an empty string for the built-in English resources.</param>
/// <param name="DisplayName">Shown in the selector, written in the language itself.</param>
public sealed record AppLanguage(string Code, string DisplayName)
{
    /// <summary>
    /// English maps to the invariant culture rather than to null. A null culture makes
    /// <see cref="System.Resources.ResourceManager"/> fall back to whatever the thread's UI culture happens
    /// to be, so on a German machine, or after the user had picked German, choosing English would keep
    /// showing German.
    /// </summary>
    public CultureInfo Culture =>
        string.IsNullOrEmpty(Code) ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(Code);

    public override string ToString() => DisplayName;
}

/// <summary>
/// Chooses, applies and remembers the interface language.
/// <para>
/// The app shipped five sets of resources with nothing in the UI to reach them, so a user who launched
/// into an unfamiliar language had no way out and a translator had no way to review their work.
/// </para>
/// </summary>
public static class LanguageService
{
    /// <summary>
    /// Every language with a satellite assembly, plus the neutral English resources compiled into the
    /// main assembly. Names are written in each language so the entry a user needs is legible to them
    /// even when the current language is one they cannot read.
    /// </summary>
    public static IReadOnlyList<AppLanguage> Available { get; } =
    [
        new("", "English"),
        new("de", "Deutsch"),
        new("es", "Español"),
        new("fr", "Français"),
        // Pseudo-locale: every string padded and bracketed. Kept in the list because reviewing
        // translations and spotting unlocalized text is exactly what the selector is for.
        new("qps-ploc", "Pseudo (qps-ploc)")
    ];

    public static AppLanguage Current { get; private set; } = Available[0];

    public static event EventHandler? LanguageChanged;

    /// <summary>
    /// Applies the stored preference, or the operating system's language when nothing has been chosen.
    /// A system language the app has no resources for falls back to English rather than showing keys.
    /// </summary>
    public static void LoadAndApply(string? storedCode)
    {
        Apply(Resolve(storedCode) ?? MatchOperatingSystem() ?? Available[0]);
    }

    public static void Apply(AppLanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);

        Current = language;
        var culture = language.Culture;

        // All three are set so a switch takes effect in every direction. Leaving the thread's UI culture
        // alone would let a previously chosen language keep answering lookups after the user picked
        // another one.
        LocalizationSource.Instance.SetCulture(culture);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;

        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Looks up a stored code. Returns null when it is absent or no longer shipped.</summary>
    public static AppLanguage? Resolve(string? code)
    {
        if (code is null)
            return null;

        return Available.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds the shipped language matching the operating system, comparing on the two-letter language so
    /// that de-AT lands on German rather than falling through to English.
    /// </summary>
    internal static AppLanguage? MatchOperatingSystem() => MatchCulture(StartupUICulture);

    /// <summary>
    /// The UI culture the process started with. Captured once because <see cref="Apply"/> overwrites the
    /// thread's culture, which would otherwise make the operating system look like whatever was last chosen.
    /// </summary>
    private static readonly CultureInfo StartupUICulture = CultureInfo.CurrentUICulture;

    internal static AppLanguage? MatchCulture(CultureInfo? osCulture)
    {
        if (osCulture is null || string.IsNullOrEmpty(osCulture.Name))
            return null;

        var exact = Available.FirstOrDefault(l =>
            !string.IsNullOrEmpty(l.Code) &&
            string.Equals(l.Code, osCulture.Name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        return Available.FirstOrDefault(l =>
            !string.IsNullOrEmpty(l.Code) &&
            string.Equals(l.Code, osCulture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));
    }
}
