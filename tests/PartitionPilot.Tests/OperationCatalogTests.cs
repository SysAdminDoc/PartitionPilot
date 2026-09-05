namespace PartitionPilot.Tests;

public class OperationCatalogTests
{
    [Fact]
    public void Search_FindsAnOperationWithoutKnowingWhichTabItIsOn()
    {
        // The whole point: the user knows the operation's name, not its tab.
        var matches = OperationCatalog.Search("hex");

        var entry = Assert.Single(matches);
        Assert.Equal(OperationCatalog.HexViewerTab, entry.TabIndex);
    }

    [Fact]
    public void Search_IsCaseInsensitive()
    {
        Assert.Equal(
            OperationCatalog.Search("MERGE").Select(e => e.NameKey),
            OperationCatalog.Search("merge").Select(e => e.NameKey));
    }

    [Fact]
    public void Search_RequiresEveryTermToMatchSoExtraWordsNarrowTheResults()
    {
        var broad = OperationCatalog.Search("disk");
        var narrow = OperationCatalog.Search("disk usage");

        Assert.True(narrow.Count < broad.Count);
        Assert.All(narrow, e => Assert.Equal(OperationCatalog.DiskUsageTab, e.TabIndex));
    }

    [Fact]
    public void Search_MatchesOnTheTabNameToo()
    {
        var matches = OperationCatalog.Search("partitions");

        Assert.NotEmpty(matches);
        Assert.All(matches, e => Assert.Equal(OperationCatalog.PartitionsTab, e.TabIndex));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Search_ReturnsEverythingForAnEmptyQuery(string? query)
    {
        Assert.Equal(OperationCatalog.All.Count, OperationCatalog.Search(query).Count);
    }

    [Fact]
    public void Search_ReturnsNothingRatherThanEverythingForANonMatch()
    {
        Assert.Empty(OperationCatalog.Search("zzzznotanoperation"));
    }

    [Fact]
    public void Every_EntryResolvesToRealLocalizedText()
    {
        // LocExtension returns "[Key]" for a key that is not in the resources, so a typo in the catalogue
        // would silently ship as bracketed text in the search list.
        Assert.All(OperationCatalog.All, entry =>
        {
            Assert.DoesNotContain('[', entry.Name);
            Assert.DoesNotContain('[', entry.Tab);
            Assert.False(string.IsNullOrWhiteSpace(entry.Name));
            Assert.False(string.IsNullOrWhiteSpace(entry.Tab));
        });
    }

    [Fact]
    public void Every_EntryPointsAtATabThatExists()
    {
        // Eight tabs in the shell; an out-of-range index would switch to nothing or throw.
        Assert.All(OperationCatalog.All, entry => Assert.InRange(entry.TabIndex, 0, 7));
    }

    [Fact]
    public void Every_TabIsReachableThroughAtLeastOneEntry()
    {
        var covered = OperationCatalog.All.Select(e => e.TabIndex).Distinct().OrderBy(i => i);

        Assert.Equal(Enumerable.Range(0, 8), covered);
    }
}
