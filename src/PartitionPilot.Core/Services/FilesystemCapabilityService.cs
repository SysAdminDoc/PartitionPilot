namespace PartitionPilot;

public enum FilesystemOperation
{
    Create,
    Format,
    Resize,
    Extend,
    Check,
    Label
}

public sealed record FilesystemCapability(
    string FileSystem,
    bool CanCreate,
    bool CanFormat,
    bool CanResize,
    bool CanExtend,
    bool CanCheck,
    bool CanLabel,
    string Notes);

public sealed record FilesystemCapabilityResult(
    string FileSystem,
    FilesystemOperation Operation,
    bool IsAllowed,
    string Reason);

public static class FilesystemCapabilityService
{
    private static readonly FilesystemCapability UnknownCapability = new(
        "Unknown",
        CanCreate: false,
        CanFormat: false,
        CanResize: false,
        CanExtend: false,
        CanCheck: false,
        CanLabel: false,
        "Unknown or unmounted filesystems are not safe for write operations.");

    private static readonly IReadOnlyList<FilesystemCapability> Matrix =
    [
        new("NTFS", true, true, true, true, true, true, "Default Windows filesystem."),
        new("FAT32", true, true, false, false, true, true, "Windows can create, format, check, and label FAT32; resize/extend is blocked."),
        new("exFAT", true, true, false, false, false, true, "Windows can create, format, and label exFAT; resize, extend, and check are blocked."),
        new("ReFS", true, true, false, true, true, true, "ReFS can be created, formatted, extended, checked, and labeled; shrink/resize is blocked."),
        new("FAT16", false, true, false, false, true, true, "Legacy FAT16 can be formatted, checked, and labeled only."),
        new("ext2/3/4", false, false, false, false, false, false, "Linux filesystem detected; Windows write support is blocked."),
        new("HFS+", false, false, false, false, false, false, "Apple filesystem detected; Windows write support is blocked."),
        new("APFS", false, false, false, false, false, false, "Apple filesystem detected; Windows write support is blocked."),
        new("Linux Swap", false, false, false, false, false, false, "Linux swap detected; Windows write support is blocked."),
        new("LUKS", false, false, false, false, false, false, "Linux encrypted volume detected; Windows write support is blocked.")
    ];

    private static readonly Dictionary<string, FilesystemCapability> ByName =
        Matrix.SelectMany(c => Aliases(c).Select(alias => (alias, c)))
            .ToDictionary(pair => pair.alias, pair => pair.c, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<FilesystemCapability> GetMatrix() => Matrix;

    public static string ValidateFormatTarget(string fileSystem)
    {
        var result = Evaluate(fileSystem, FilesystemOperation.Format);
        if (!result.IsAllowed)
            throw new ArgumentException(result.Reason, nameof(fileSystem));

        return result.FileSystem;
    }

    public static FilesystemCapabilityResult Evaluate(string? fileSystem, FilesystemOperation operation)
    {
        var capability = GetCapability(fileSystem);
        var allowed = operation switch
        {
            FilesystemOperation.Create => capability.CanCreate,
            FilesystemOperation.Format => capability.CanFormat,
            FilesystemOperation.Resize => capability.CanResize,
            FilesystemOperation.Extend => capability.CanExtend,
            FilesystemOperation.Check => capability.CanCheck,
            FilesystemOperation.Label => capability.CanLabel,
            _ => false
        };

        return new FilesystemCapabilityResult(
            capability.FileSystem,
            operation,
            allowed,
            allowed
                ? $"{operation} is supported for {capability.FileSystem}."
                : $"{operation} is not supported for {capability.FileSystem}. {capability.Notes}");
    }

    public static FilesystemCapability GetCapability(string? fileSystem)
    {
        var normalized = Normalize(fileSystem);
        if (string.IsNullOrWhiteSpace(normalized))
            return UnknownCapability;

        return ByName.TryGetValue(normalized, out var capability)
            ? capability
            : UnknownCapability with { FileSystem = normalized, Notes = $"{normalized} is not in the supported Windows filesystem policy." };
    }

    private static string Normalize(string? fileSystem)
    {
        if (string.IsNullOrWhiteSpace(fileSystem))
            return "";

        var value = fileSystem.Trim();
        return value.Equals("FAT", StringComparison.OrdinalIgnoreCase)
            ? "FAT16"
            : value;
    }

    private static IEnumerable<string> Aliases(FilesystemCapability capability)
    {
        yield return capability.FileSystem;

        if (capability.FileSystem == "ext2/3/4")
        {
            yield return "ext2";
            yield return "ext3";
            yield return "ext4";
            yield return "Linux";
            yield return "Linux Root (x86-64)";
            yield return "Linux Home";
        }

        if (capability.FileSystem == "FAT16")
        {
            yield return "FAT";
            yield return "FAT12";
        }
    }
}
