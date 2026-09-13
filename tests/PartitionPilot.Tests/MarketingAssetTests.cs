using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PartitionPilot.Tests;

public sealed class MarketingAssetTests
{
    private const string HeroPath = "assets/marketing/readme-hero.png";
    private const string HeroMarkdown = "![PartitionPilot guarded Windows disk management workspace](assets/marketing/readme-hero.png)";

    [Fact]
    public void ReadmeStartsWithExactlyOneHeroReference()
    {
        var repoRoot = FindRepoRoot();
        var readme = File.ReadAllText(Path.Combine(repoRoot, "README.md")).TrimStart('\uFEFF');

        Assert.StartsWith(HeroMarkdown, readme, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(readme, Regex.Escape(HeroPath), RegexOptions.CultureInvariant).Cast<Match>());
        Assert.DoesNotContain("assets/screenshots/01-partition-workspace.png", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionHeroMatchesTheSelectedArchivedFinal()
    {
        var repoRoot = FindRepoRoot();
        var production = Path.Combine(repoRoot, "assets", "marketing", "readme-hero.png");
        var socialPreview = Path.Combine(repoRoot, "assets", "social-preview.png");
        var selected = Path.Combine(
            repoRoot,
            "assets",
            "concepts",
            "2026-09-12-readme-hero",
            "readme-hero-final.png");

        Assert.Equal(Sha256(selected), Sha256(production));
        Assert.Equal(Sha256(selected), Sha256(socialPreview));

        var header = File.ReadAllBytes(production);
        Assert.True(header.Length > 26);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, header[..8]);
        Assert.Equal(1280, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4)));
        Assert.Equal(640, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4)));
        Assert.Equal(2, header[25]);
    }

    [Fact]
    public void HeroCopyContainsNoReleaseNumber()
    {
        var copy = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "assets",
            "marketing",
            "readme-hero-copy.txt"));

        Assert.DoesNotMatch(
            new Regex(@"\bv?\d+\.\d+\.\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            copy);
        Assert.DoesNotContain("version", copy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HeroDecisionArchivePreservesTheFullReviewTrail()
    {
        var archive = Path.Combine(
            FindRepoRoot(),
            "assets",
            "concepts",
            "2026-09-12-readme-hero");

        Assert.True(File.Exists(Path.Combine(archive, "README-before.md")));
        Assert.True(File.Exists(Path.Combine(archive, "source", "previous-social-preview-versioned.png")));
        Assert.True(File.Exists(Path.Combine(archive, "source", "approved-logo-master.png")));
        Assert.True(File.Exists(Path.Combine(archive, "source", "approved-brand-concepts", "direction-01-selected-partition-flow.png")));
        Assert.True(File.Exists(Path.Combine(archive, "source", "approved-brand-concepts", "direction-02-partition-scale.png")));
        Assert.True(File.Exists(Path.Combine(archive, "source", "approved-brand-concepts", "direction-03-partition-flow-outline.png")));
        Assert.True(File.Exists(Path.Combine(archive, "readme-hero-candidate-01-continuity.png")));
        Assert.True(File.Exists(Path.Combine(archive, "readme-hero-candidate-02-safety-led.png")));
        Assert.True(File.Exists(Path.Combine(archive, "review", "reference-and-candidates.png")));
        Assert.True(File.Exists(Path.Combine(archive, "review", "selected-640.png")));
        Assert.True(File.Exists(Path.Combine(archive, "review", "selected-960.png")));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(FindRepoRoot(), "README.md")),
            File.ReadAllBytes(Path.Combine(archive, "README-after.md")));
    }

    [Fact]
    public void HeroRenderReportRecordsAReviewedVersionFreeCrop()
    {
        var reportPath = Path.Combine(
            FindRepoRoot(),
            "assets",
            "concepts",
            "2026-09-12-readme-hero",
            "render-report.json");
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;

        Assert.Equal("candidate-2", root.GetProperty("selectedCandidate").GetString());
        Assert.False(root.GetProperty("containsReleaseNumber").GetBoolean());
        Assert.True(root.GetProperty("screenshotCrop").GetProperty("excludesApplicationVersion").GetBoolean());

        using var selection = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "assets",
            "concepts",
            "2026-09-12-readme-hero",
            "selection.json")));
        Assert.Equal(
            root.GetProperty("final").GetProperty("sha256").GetString(),
            selection.RootElement.GetProperty("sha256").GetString());
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "README.md")) &&
                File.Exists(Path.Combine(current.FullName, "src", "PartitionPilot", "PartitionPilot.csproj")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not find the PartitionPilot repository root.");
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
