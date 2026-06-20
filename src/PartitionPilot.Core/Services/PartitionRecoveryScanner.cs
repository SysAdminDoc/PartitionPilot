using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PartitionPilot;

public class CandidatePartition
{
    public long Offset { get; set; }
    public long EstimatedSize { get; set; }
    public string FileSystem { get; set; } = "Unknown";
    public string SignatureType { get; set; } = "";
    public string Details { get; set; } = "";
    public double Confidence { get; set; }

    public string OffsetText => SizeUtil.Format(Offset);
    public string SizeText => EstimatedSize > 0 ? SizeUtil.Format(EstimatedSize) : "Unknown";
    public string ConfidenceText => $"{Confidence:F0}%";
}

public class RecoveryScanResult
{
    public int DiskNumber { get; set; }
    public long DiskSize { get; set; }
    public int CandidateCount { get; set; }
    public List<CandidatePartition> Candidates { get; set; } = new();
    public DateTimeOffset ScannedAt { get; set; }
    public TimeSpan Duration { get; set; }
}

public static class PartitionRecoveryScanner
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x01;
    private const uint FILE_SHARE_WRITE = 0x02;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
    private const int SECTOR_SIZE = 512;
    private const int BUFFER_SIZE = 4096;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        IntPtr hFile, byte[] lpBuffer, int nNumberOfBytesToRead,
        out int lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFilePointerEx(
        IntPtr hFile, long liDistanceToMove, out long lpNewFilePointer, uint dwMoveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static readonly byte[] NtfsSignature = "NTFS    "u8.ToArray();
    private static readonly byte[] Fat32Signature = "FAT32   "u8.ToArray();
    private static readonly byte[] Fat16Signature = "FAT16   "u8.ToArray();
    private static readonly byte[] Fat12Signature = "FAT12   "u8.ToArray();
    private static readonly byte[] ExfatSignature = "EXFAT   "u8.ToArray();
    private static readonly byte[] RefsMagic = [0x52, 0x65, 0x46, 0x53]; // "ReFS"
    private static readonly byte[] Ext2Magic = [0x53, 0xEF]; // ext2/3/4 magic at superblock offset 56
    private static readonly byte[] XfsMagic = "XFSB"u8.ToArray(); // XFS superblock magic at offset 0
    private static readonly byte[] BtrfsMagic = "_BHRfS_M"u8.ToArray(); // btrfs magic at superblock offset 64
    private static readonly byte[] HfsPlusMagic = [0x48, 0x2B]; // "H+" at offset 0 of volume header
    private static readonly byte[] HfsxMagic = [0x48, 0x58]; // "HX" for HFSX
    private static readonly byte[] ApfsNxMagic = "NXSB"u8.ToArray(); // APFS container superblock
    private static readonly byte[] SwapMagic = "SWAPSPACE2"u8.ToArray(); // Linux swap at page end - 10

    public static async Task<RecoveryScanResult> ScanAsync(int diskNumber, long diskSize,
        IActivityLog log, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        log.Log($"Starting read-only partition recovery scan on disk {diskNumber} ({SizeUtil.Format(diskSize)})...");

        var result = new RecoveryScanResult
        {
            DiskNumber = diskNumber,
            DiskSize = diskSize,
            ScannedAt = DateTimeOffset.UtcNow
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var candidates = await Task.Run(() => ScanDisk(diskNumber, diskSize, log, progress, ct), ct);

        sw.Stop();
        result.Duration = sw.Elapsed;
        result.Candidates = candidates;
        result.CandidateCount = candidates.Count;

        log.Log($"Recovery scan complete: found {candidates.Count} candidate(s) in {sw.Elapsed.TotalSeconds:F1}s.");
        return result;
    }

    private static List<CandidatePartition> ScanDisk(int diskNumber, long diskSize,
        IActivityLog log, IProgress<double>? progress, CancellationToken ct)
    {
        var path = $"\\\\.\\PhysicalDrive{diskNumber}";
        var handle = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_NO_BUFFERING, IntPtr.Zero);

        if (handle == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot open disk {diskNumber} for read-only scan");

        try
        {
            return ScanSectors(handle, diskSize, log, progress, ct);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static List<CandidatePartition> ScanSectors(IntPtr handle, long diskSize,
        IActivityLog log, IProgress<double>? progress, CancellationToken ct)
    {
        var candidates = new List<CandidatePartition>();
        var buffer = new byte[BUFFER_SIZE];
        long offset = 0;
        long stepSize = SECTOR_SIZE;
        long lastProgressReport = 0;

        while (offset < diskSize)
        {
            ct.ThrowIfCancellationRequested();

            if (!SetFilePointerEx(handle, offset, out _, 0))
                break;

            if (!ReadFile(handle, buffer, BUFFER_SIZE, out int bytesRead, IntPtr.Zero) || bytesRead < SECTOR_SIZE)
                break;

            var candidate = CheckSignatures(buffer, offset);
            if (candidate is not null)
            {
                candidates.Add(candidate);
                log.Log($"  Found {candidate.FileSystem} signature at offset {SizeUtil.Format(offset)} ({candidate.Confidence:F0}% confidence)");
            }

            if (offset - lastProgressReport > 1_073_741_824)
            {
                progress?.Report((double)offset / diskSize * 100);
                lastProgressReport = offset;
            }

            offset += stepSize;
        }

        progress?.Report(100);
        return candidates;
    }

    private static CandidatePartition? CheckSignatures(byte[] buffer, long offset)
    {
        if (buffer[510] == 0x55 && buffer[511] == 0xAA)
        {
            if (MatchesAt(buffer, 3, NtfsSignature))
            {
                long totalSectors = BitConverter.ToInt64(buffer, 0x28);
                long bytesPerSector = BitConverter.ToInt16(buffer, 0x0B);
                long estimatedSize = totalSectors > 0 && bytesPerSector > 0 ? totalSectors * bytesPerSector : 0;

                return new CandidatePartition
                {
                    Offset = offset,
                    FileSystem = "NTFS",
                    SignatureType = "VBR (Volume Boot Record)",
                    EstimatedSize = estimatedSize,
                    Confidence = 95,
                    Details = $"NTFS VBR at sector {offset / SECTOR_SIZE}, {(estimatedSize > 0 ? SizeUtil.Format(estimatedSize) : "size unknown")}"
                };
            }

            if (MatchesAt(buffer, 82, Fat32Signature))
            {
                long totalSectors32 = BitConverter.ToUInt32(buffer, 0x20);
                int bytesPerSector = BitConverter.ToInt16(buffer, 0x0B);
                long estimatedSize = totalSectors32 > 0 && bytesPerSector > 0 ? totalSectors32 * bytesPerSector : 0;

                return new CandidatePartition
                {
                    Offset = offset,
                    FileSystem = "FAT32",
                    SignatureType = "VBR (Volume Boot Record)",
                    EstimatedSize = estimatedSize,
                    Confidence = 90,
                    Details = $"FAT32 VBR at sector {offset / SECTOR_SIZE}"
                };
            }

            if (MatchesAt(buffer, 54, Fat16Signature) || MatchesAt(buffer, 54, Fat12Signature))
            {
                var fsType = MatchesAt(buffer, 54, Fat16Signature) ? "FAT16" : "FAT12";
                long totalSectors16 = BitConverter.ToUInt16(buffer, 0x13);
                if (totalSectors16 == 0) totalSectors16 = BitConverter.ToUInt32(buffer, 0x20);
                int bytesPerSector = BitConverter.ToInt16(buffer, 0x0B);
                long estimatedSize = totalSectors16 > 0 && bytesPerSector > 0 ? totalSectors16 * bytesPerSector : 0;

                return new CandidatePartition
                {
                    Offset = offset,
                    FileSystem = fsType,
                    SignatureType = "VBR (Volume Boot Record)",
                    EstimatedSize = estimatedSize,
                    Confidence = 85,
                    Details = $"{fsType} VBR at sector {offset / SECTOR_SIZE}"
                };
            }
        }

        if (MatchesAt(buffer, 3, ExfatSignature))
        {
            long volumeLength = BitConverter.ToInt64(buffer, 0x48);
            long estimatedSize = volumeLength > 0 ? volumeLength * SECTOR_SIZE : 0;

            return new CandidatePartition
            {
                Offset = offset,
                FileSystem = "exFAT",
                SignatureType = "VBR (Volume Boot Record)",
                EstimatedSize = estimatedSize,
                Confidence = 90,
                Details = $"exFAT VBR at sector {offset / SECTOR_SIZE}"
            };
        }

        if (buffer.Length >= 8 && MatchesAt(buffer, 0, RefsMagic))
        {
            return new CandidatePartition
            {
                Offset = offset,
                FileSystem = "ReFS",
                SignatureType = "Superblock",
                Confidence = 70,
                Details = $"ReFS signature at sector {offset / SECTOR_SIZE}"
            };
        }

        if (buffer.Length >= 4 && MatchesAt(buffer, 0, XfsMagic))
        {
            long blockSize = buffer.Length >= 8 ? (long)buffer[4] << 24 | (long)buffer[5] << 16 | (long)buffer[6] << 8 | buffer[7] : 0;
            long totalBlocks = buffer.Length >= 16 ?
                (long)buffer[8] << 56 | (long)buffer[9] << 48 | (long)buffer[10] << 40 | (long)buffer[11] << 32 |
                (long)buffer[12] << 24 | (long)buffer[13] << 16 | (long)buffer[14] << 8 | buffer[15] : 0;
            long estimatedSize = blockSize > 0 && totalBlocks > 0 ? blockSize * totalBlocks : 0;

            return new CandidatePartition
            {
                Offset = offset,
                FileSystem = "XFS",
                SignatureType = "Superblock",
                EstimatedSize = estimatedSize,
                Confidence = 85,
                Details = $"XFS superblock at sector {offset / SECTOR_SIZE}"
            };
        }

        if (buffer.Length >= 4 && MatchesAt(buffer, 0, ApfsNxMagic))
        {
            return new CandidatePartition
            {
                Offset = offset,
                FileSystem = "APFS",
                SignatureType = "Container Superblock",
                Confidence = 80,
                Details = $"APFS container superblock at sector {offset / SECTOR_SIZE}"
            };
        }

        if (buffer.Length >= 1082 && MatchesAt(buffer, 1024 + 56, Ext2Magic))
        {
            int logBlockSize = BitConverter.ToInt32(buffer, 1024 + 24);
            uint totalBlocks = BitConverter.ToUInt32(buffer, 1024 + 4);
            long blockSize = 1024L << logBlockSize;
            long estimatedSize = totalBlocks > 0 && blockSize > 0 ? totalBlocks * blockSize : 0;

            bool hasJournal = (BitConverter.ToUInt32(buffer, 1024 + 96) & 0x04) != 0;
            bool hasExtents = (BitConverter.ToUInt32(buffer, 1024 + 96) & 0x40) != 0;
            string fsType = hasExtents ? "ext4" : hasJournal ? "ext3" : "ext2";

            return new CandidatePartition
            {
                Offset = offset,
                FileSystem = fsType,
                SignatureType = "Superblock",
                EstimatedSize = estimatedSize,
                Confidence = 90,
                Details = $"{fsType} superblock at offset {offset + 1024}"
            };
        }

        if (buffer.Length >= 1026 && (MatchesAt(buffer, 1024, HfsPlusMagic) || MatchesAt(buffer, 1024, HfsxMagic)))
        {
            var fsType = MatchesAt(buffer, 1024, HfsxMagic) ? "HFSX" : "HFS+";
            return new CandidatePartition
            {
                Offset = offset,
                FileSystem = fsType,
                SignatureType = "Volume Header",
                Confidence = 80,
                Details = $"{fsType} volume header at offset {offset + 1024}"
            };
        }

        if (buffer.Length >= 72 && MatchesAt(buffer, 64, BtrfsMagic))
        {
            return new CandidatePartition
            {
                Offset = offset,
                FileSystem = "btrfs",
                SignatureType = "Superblock",
                Confidence = 85,
                Details = $"btrfs superblock at offset {offset + 64}"
            };
        }

        if (buffer.Length >= 4096 && MatchesAt(buffer, 4086, SwapMagic))
        {
            return new CandidatePartition
            {
                Offset = offset,
                FileSystem = "Linux Swap",
                SignatureType = "Swap Header",
                Confidence = 80,
                Details = $"Linux swap signature at sector {offset / SECTOR_SIZE}"
            };
        }

        return null;
    }

    private static bool MatchesAt(byte[] buffer, int offset, byte[] pattern)
    {
        if (offset + pattern.Length > buffer.Length) return false;
        for (int i = 0; i < pattern.Length; i++)
        {
            if (buffer[offset + i] != pattern[i]) return false;
        }
        return true;
    }

    public static string FormatReport(RecoveryScanResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Partition Recovery Scan — Disk {result.DiskNumber} ({SizeUtil.Format(result.DiskSize)})");
        sb.AppendLine($"Scanned at: {result.ScannedAt:yyyy-MM-dd HH:mm:ss} in {result.Duration.TotalSeconds:F1}s");
        sb.AppendLine($"Found {result.CandidateCount} candidate partition(s)");
        sb.AppendLine();

        if (result.Candidates.Count == 0)
        {
            sb.AppendLine("No filesystem signatures found in the scanned sectors.");
            return sb.ToString();
        }

        sb.AppendLine($"{"#",-4} {"FS",-8} {"Offset",-16} {"Size",-14} {"Confidence",-12} Details");
        sb.AppendLine(new string('-', 80));

        for (int i = 0; i < result.Candidates.Count; i++)
        {
            var c = result.Candidates[i];
            sb.AppendLine($"{i + 1,-4} {c.FileSystem,-8} {c.OffsetText,-16} {c.SizeText,-14} {c.ConfidenceText,-12} {c.Details}");
        }

        sb.AppendLine();
        sb.AppendLine("NOTE: This is a read-only scan. No partition table changes have been made.");
        sb.AppendLine("Export this report as evidence for manual recovery planning.");

        return sb.ToString();
    }

    public static string FormatJson(RecoveryScanResult result) =>
        JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
}
