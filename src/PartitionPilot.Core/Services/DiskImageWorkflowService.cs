using System.IO;

namespace PartitionPilot;

public sealed record ImageDestinationPreflight(
    string FullPath,
    string DestinationRoot,
    long EstimatedRequiredBytes,
    long DestinationFreeBytes);

public static class DiskImageWorkflowService
{
    public static long EstimateImageBytes(long sourceSizeBytes, long sourceFreeBytes)
    {
        if (sourceSizeBytes <= 0) return 0;

        var hasUsableFreeSpace = sourceFreeBytes >= 0 && sourceFreeBytes <= sourceSizeBytes;
        var usedBytes = hasUsableFreeSpace ? sourceSizeBytes - sourceFreeBytes : sourceSizeBytes;
        var minimumImageBytes = 1L << 30;
        var overheadBytes = 512L * 1024L * 1024L;
        var estimatedBytes = Math.Max(usedBytes, minimumImageBytes);

        return estimatedBytes > long.MaxValue - overheadBytes
            ? long.MaxValue
            : estimatedBytes + overheadBytes;
    }

    public static ImageDestinationPreflight PreflightDestination(
        string imagePath,
        char sourceDrive,
        long estimatedRequiredBytes,
        Func<string, bool> directoryExists,
        Func<string, bool> fileExists,
        Func<string, long> getAvailableFreeSpace)
    {
        if (sourceDrive == default)
            throw new InvalidOperationException("Select a source volume before creating an image.");

        if (string.IsNullOrWhiteSpace(imagePath))
            throw new InvalidOperationException("Choose a destination path before creating an image.");

        var trimmedPath = imagePath.Trim();
        if (!Path.IsPathFullyQualified(trimmedPath))
            throw new InvalidOperationException("Choose a fully qualified destination path.");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(trimmedPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException($"The destination path is invalid: {ex.Message}", ex);
        }

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is not ".wim" and not ".vhdx")
            throw new InvalidOperationException("Choose a .wim or .vhdx destination file.");

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root) || !directoryExists(root))
            throw new InvalidOperationException("The destination drive or share is not available.");

        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(parent) || !directoryExists(parent))
            throw new InvalidOperationException("Create the destination folder before starting the image capture.");

        if (fileExists(fullPath))
            throw new InvalidOperationException("Choose a new image path or delete the existing file first.");

        if (TryGetDriveLetter(root) is { } destinationDrive &&
            char.ToUpperInvariant(destinationDrive) == char.ToUpperInvariant(sourceDrive))
        {
            throw new InvalidOperationException("Choose a destination outside the source volume; capturing a volume into itself is unsafe.");
        }

        long availableBytes;
        try
        {
            availableBytes = getAvailableFreeSpace(root);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not read free space for the destination: {ex.Message}", ex);
        }

        if (estimatedRequiredBytes > 0 && availableBytes < estimatedRequiredBytes)
        {
            throw new InvalidOperationException(
                $"The destination has {SizeUtil.Format(availableBytes)} free, but the image may require up to {SizeUtil.Format(estimatedRequiredBytes)}.");
        }

        return new ImageDestinationPreflight(fullPath, root, Math.Max(estimatedRequiredBytes, 0), availableBytes);
    }

    public static void GuardSourceVolumeForCapture(
        char sourceDrive,
        IReadOnlyDictionary<char, string> bitLockerByLetter)
    {
        var key = char.ToUpperInvariant(sourceDrive);
        if (!bitLockerByLetter.TryGetValue(key, out var status) || !BitLockerPreflight.RequiresUnlockForRead(status))
            return;

        throw new InvalidOperationException(
            BitLockerPreflight.BuildUnlockRequiredMessage($"Create an image from {key}:", $"{key}:", status));
    }

    private static char? TryGetDriveLetter(string root) =>
        root.Length >= 2 && root[1] == ':' && char.IsLetter(root[0])
            ? char.ToUpperInvariant(root[0])
            : null;
}
