namespace PartitionPilot;

/// <summary>One findable operation and the tab that owns it.</summary>
/// <param name="NameKey">Localization key for the operation's display name.</param>
/// <param name="TabKey">Localization key for the owning tab.</param>
/// <param name="TabIndex">Index of the owning tab in the shell's TabControl.</param>
public sealed record OperationEntry(string NameKey, string TabKey, int TabIndex)
{
    public string Name => LocExtension.Get(NameKey);
    public string Tab => LocExtension.Get(TabKey);
    public string Display => $"{Name}  —  {Tab}";
}

/// <summary>
/// The operations a user can search for, and which tab each lives on.
/// <para>
/// Eight tabs hold roughly forty operations with no way to find one by name, so an operator who knows
/// they want "Dev Drive" or "NVMe sanitize" has to remember which tab it is on. Names come from the
/// localization keys the tabs themselves use, so the catalogue is translated with the rest of the shell
/// and cannot drift into English-only text.
/// </para>
/// </summary>
public static class OperationCatalog
{
    public const int PartitionsTab = 0;
    public const int SnapshotsTab = 1;
    public const int DiskHealthTab = 2;
    public const int ToolsTab = 3;
    public const int DiskImagesTab = 4;
    public const int DiskUsageTab = 5;
    public const int DiskCloningTab = 6;
    public const int HexViewerTab = 7;

    public static IReadOnlyList<OperationEntry> All { get; } =
    [
        new("Create", "TabPartitions", PartitionsTab),
        new("Delete", "TabPartitions", PartitionsTab),
        new("Format", "TabPartitions", PartitionsTab),
        new("Resize", "TabPartitions", PartitionsTab),
        new("Extend", "TabPartitions", PartitionsTab),
        new("Split", "TabPartitions", PartitionsTab),
        new("Merge", "TabPartitions", PartitionsTab),
        new("ChangeLetter", "TabPartitions", PartitionsTab),

        new("RecoveryPlan", "TabSnapshots", SnapshotsTab),
        new("RestoreSnapshot", "TabSnapshots", SnapshotsTab),
        new("PreviewRestore", "TabSnapshots", SnapshotsTab),

        new("TabDiskHealth", "TabDiskHealth", DiskHealthTab),

        new("TabTools", "TabTools", ToolsTab),

        new("TabDiskImages", "TabDiskImages", DiskImagesTab),
        new("TabDiskUsage", "TabDiskUsage", DiskUsageTab),
        new("TabDiskCloning", "TabDiskCloning", DiskCloningTab),
        new("TabHexViewer", "TabHexViewer", HexViewerTab)
    ];

    /// <summary>
    /// Filters the catalogue. Matching is on the localized name and tab so a user searching in their own
    /// language finds the operation they can actually see on screen.
    /// </summary>
    public static IReadOnlyList<OperationEntry> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return All;

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return All
            .Where(entry => terms.All(term =>
                entry.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Tab.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();
    }
}
