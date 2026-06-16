namespace PartitionPilot;

public static class SizeUtil
{
    public static string Format(long bytes)
    {
        if (bytes >= 1L << 40) return $"{Math.Round(bytes / (double)(1L << 40), 2)} TB";
        if (bytes >= 1L << 30) return $"{Math.Round(bytes / (double)(1L << 30), 2)} GB";
        if (bytes >= 1L << 20) return $"{Math.Round(bytes / (double)(1L << 20), 0)} MB";
        return $"{Math.Round(bytes / 1024.0, 0)} KB";
    }
}
