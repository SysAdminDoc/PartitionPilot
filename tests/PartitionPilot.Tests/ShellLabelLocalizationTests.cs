using System.Text.RegularExpressions;

namespace PartitionPilot.Tests;

/// <summary>
/// The XAML localization gate only sees strings declared in markup, so user-visible text assigned from a
/// view model went untranslated for as long as there was no way to change language. These cover the
/// shell labels that come from code.
/// </summary>
[Collection(LocalizationCollection.Name)]
public class ShellLabelLocalizationTests : IDisposable
{
    private readonly AppLanguage _original = LanguageService.Current;

    [Theory]
    [InlineData("ThemeLightMode")]
    [InlineData("ThemeSystemTheme")]
    [InlineData("ThemeDarkMode")]
    [InlineData("SessionStateLabel")]
    [InlineData("AdminSession")]
    [InlineData("ReadOnlySession")]
    [InlineData("AdminSessionDetail")]
    [InlineData("ReadOnlySessionDetail")]
    [InlineData("ElevationStandard")]
    [InlineData("ElevationAdminProtection")]
    [InlineData("ElevationLegacyUac")]
    public void EveryShellLabelKey_TranslatesInEveryShippedLanguage(string key)
    {
        foreach (var language in LanguageService.Available)
        {
            LanguageService.Apply(language);
            var value = LocExtension.Get(key);

            Assert.False(string.IsNullOrWhiteSpace(value));
            // LocExtension returns "[Key]" when a key is missing from a locale's resources.
            Assert.NotEqual($"[{key}]", value);
        }
    }

    [Fact]
    public void ThemeLabel_FollowsTheInterfaceLanguage()
    {
        // This is the label sitting directly beside the language selector, so leaving it in English is
        // the most visible way the feature could look broken.
        LanguageService.Apply(English);
        var english = ThemeService.GetLabel();

        LanguageService.Apply(German);
        var german = ThemeService.GetLabel();

        Assert.False(string.IsNullOrWhiteSpace(english));
        Assert.NotEqual(english, german);
    }

    [Theory]
    [InlineData(true, "ElevationAdminProtection", "ElevationLegacyUac")]
    [InlineData(false, "ElevationStandard", "ElevationStandard")]
    public void ElevationContext_ResolvesToAKeyRatherThanAnEnglishLiteral(
        bool isAdmin, string firstAcceptable, string secondAcceptable)
    {
        var key = MainViewModel.DetectElevationContextKey(isAdmin);

        Assert.Contains(key, new[] { firstAcceptable, secondAcceptable });
        Assert.NotEqual($"[{key}]", LocExtension.Get(key));
    }

    [Fact]
    public void NoShellLabelIsAssignedAsAnEnglishLiteral()
    {
        // Guards the regression this item fixed: a future label assigned as a bare string would be
        // invisible to the XAML gate and would silently stay English for every translated user.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "PartitionPilot", "ViewModels", "MainViewModel.cs"));

        var offenders = Regex.Matches(source, @"(?<prop>AdminSessionText|AdminSessionDetail|SessionStateText|ElevationContextText)\s*=\s*""")
            .Select(m => m.Groups["prop"].Value)
            .Distinct()
            .ToList();

        Assert.Empty(offenders);
    }

    private static AppLanguage English => LanguageService.Resolve("")!;
    private static AppLanguage German => LanguageService.Resolve("de")!;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LICENSE")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    public void Dispose()
    {
        LanguageService.Apply(_original);
        GC.SuppressFinalize(this);
    }
}
