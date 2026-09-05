using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PartitionPilot.Tests;

/// <summary>
/// Guards the format strings behind <see cref="LocExtension.Format"/>.
/// <para>
/// A translation whose placeholders do not match the English original is the dangerous kind of typo:
/// most of these strings report failures, so a mismatched <c>{0}</c> would turn a readable error into a
/// crash inside the code that was trying to explain the first one.
/// </para>
/// </summary>
public class ResourcePlaceholderTests
{
    private static readonly Regex Placeholder = new(@"\{(?<index>\d+)(?:[,:][^}]*)?\}", RegexOptions.Compiled);

    public static TheoryData<string> TranslatedFiles() =>
        ["Strings.de.resx", "Strings.es.resx", "Strings.fr.resx", "Strings.qps-ploc.resx"];

    [Theory]
    [MemberData(nameof(TranslatedFiles))]
    public void EveryTranslation_UsesTheSamePlaceholdersAsEnglish(string translatedFile)
    {
        var english = ReadResources("Strings.resx");
        var translated = ReadResources(translatedFile);

        var mismatches = new List<string>();

        foreach (var (key, englishValue) in english)
        {
            if (!translated.TryGetValue(key, out var translatedValue))
                continue; // parity is the localization gate's job, not this test's

            var expected = PlaceholderIndexes(englishValue);
            var actual = PlaceholderIndexes(translatedValue);

            if (!expected.SetEquals(actual))
                mismatches.Add($"{translatedFile}:{key} expected {{{string.Join(",", expected.Order())}}} " +
                               $"but has {{{string.Join(",", actual.Order())}}}");
        }

        Assert.Empty(mismatches);
    }

    [Theory]
    [MemberData(nameof(TranslatedFiles))]
    public void EveryTranslation_FormatsWithoutThrowing(string translatedFile)
    {
        // The fallback in LocExtension.Format hides a bad string at runtime; this makes it visible here.
        var failures = new List<string>();

        foreach (var (key, value) in ReadResources(translatedFile))
        {
            var indexes = PlaceholderIndexes(value);
            if (indexes.Count == 0)
                continue;

            var args = Enumerable.Range(0, indexes.Max() + 1).Select(object? (i) => $"arg{i}").ToArray();

            try
            {
                _ = string.Format(System.Globalization.CultureInfo.InvariantCulture, value, args);
            }
            catch (FormatException ex)
            {
                failures.Add($"{translatedFile}:{key} — {ex.Message}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void Format_SubstitutesRuntimeValues()
    {
        Assert.Contains("42", LocExtension.Format("HexReadingSector", 42));
    }

    [Fact]
    public void Format_FallsBackToTheTemplateRatherThanThrowingOnAMismatch()
    {
        // Deliberately too few arguments for the template's placeholders.
        var result = LocExtension.Format("HexSectorRead");

        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    private static HashSet<int> PlaceholderIndexes(string value) =>
        Placeholder.Matches(value)
            .Select(m => int.Parse(m.Groups["index"].Value))
            .ToHashSet();

    private static Dictionary<string, string> ReadResources(string fileName)
    {
        var path = Path.Combine(RepoRoot(), "src", "PartitionPilot", "Properties", fileName);

        return XDocument.Load(path).Root!
            .Elements("data")
            .Where(d => d.Attribute("name") is not null)
            .ToDictionary(
                d => d.Attribute("name")!.Value,
                d => d.Element("value")?.Value ?? "");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LICENSE")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
