using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PartitionPilot;

public enum RecoveryScanMode
{
    Fast,
    Deep
}

public sealed class RecoveryScanOptions
{
    public RecoveryScanMode Mode { get; init; } = RecoveryScanMode.Fast;
    public string? ResumeStatePath { get; init; }
    public long CheckpointIntervalBytes { get; init; } = 1L * 1024 * 1024 * 1024;
}

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
    public string ScanMode { get; set; } = RecoveryScanMode.Fast.ToString();
    public long CheckedOffsetCount { get; set; }
    public long CoverageBytes { get; set; }
    public double CoveragePercent => DiskSize > 0 ? Math.Min(100, (double)CoverageBytes / DiskSize * 100) : 0;
    public long LastScannedOffset { get; set; }
    public bool IsComplete { get; set; } = true;
    public string? ResumeStatePath { get; set; }
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
    private const long MiB = 1024L * 1024;
    private const long GiB = 1024L * MiB;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

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
    private static readonly byte[] RefsMagic = [0x52, 0x65, 0x46, 0x53];
    private static readonly byte[] Ext2Magic = [0x53, 0xEF];
    private static readonly byte[] XfsMagic = "XFSB"u8.ToArray();
    private static readonly byte[] BtrfsMagic = "_BHRfS_M"u8.ToArray();
    private static readonly byte[] HfsPlusMagic = [0x48, 0x2B];
    private static readonly byte[] HfsxMagic = [0x48, 0x58];
    private static readonly byte[] ApfsNxMagic = "NXSB"u8.ToArray();
    private static readonly byte[] SwapMagic = "SWAPSPACE2"u8.ToArray();

    public static Task<RecoveryScanResult> ScanAsync(int diskNumber, long diskSize,
        IActivityLog log, IProgress<double>? progress = null, CancellationToken ct = default) =>
        ScanAsync(diskNumber, diskSize, log, new RecoveryScanOptions(), progress, ct);

    public static async Task<RecoveryScanResult> ScanAsync(int diskNumber, long diskSize,
        IActivityLog log, RecoveryScanOptions? options, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        options ??= new RecoveryScanOptions();
        var statePath = options.Mode == RecoveryScanMode.Deep
            ? ResolveResumeStatePath(diskNumber, options.ResumeStatePath)
            : options.ResumeStatePath;

        log.Log($"Starting read-only {options.Mode.ToString().ToLowerInvariant()} partition recovery scan on disk {diskNumber} ({SizeUtil.Format(diskSize)})...");

        var result = new RecoveryScanResult
        {
            DiskNumber = diskNumber,
            DiskSize = diskSize,
            ScanMode = options.Mode.ToString(),
            ResumeStatePath = statePath,
            ScannedAt = DateTimeOffset.UtcNow
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var work = await Task.Run(() => ScanDisk(diskNumber, diskSize, log, options, statePath, progress, ct), ct);

        sw.Stop();
        result.Duration = sw.Elapsed;
        result.Candidates = work.Candidates;
        result.CandidateCount = work.Candidates.Count;
        result.CheckedOffsetCount = work.CheckedOffsetCount;
        result.CoverageBytes = work.CoverageBytes;
        result.LastScannedOffset = work.LastScannedOffset;
        result.IsComplete = work.IsComplete;

        if (work.IsComplete && options.Mode == RecoveryScanMode.Deep && !string.IsNullOrWhiteSpace(statePath) && File.Exists(statePath))
        {
            File.Delete(statePath);
            log.Log($"Removed completed recovery scan resume state: {statePath}");
        }

        log.Log($"Recovery scan complete: found {result.CandidateCount} candidate(s), checked {result.CheckedOffsetCount:N0} offset(s), coverage {result.CoveragePercent:F2}% in {sw.Elapsed.TotalSeconds:F1}s.");
        return result;
    }

    public static string GetDefaultResumeStatePath(int diskNumber)
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PartitionPilot", "recovery");

        try
        {
            Directory.CreateDirectory(baseDir);
        }
        catch
        {
            baseDir = Path.Combine(Path.GetTempPath(), "PartitionPilot", "recovery");
            Directory.CreateDirectory(baseDir);
        }

        return Path.Combine(baseDir, $"recovery-scan-disk{diskNumber}.json");
    }

    public static IReadOnlyList<long> BuildFastProbeOffsets(long diskSize)
    {
        var offsets = new List<long>();
        var seen = new HashSet<long>();

        void Add(long offset)
        {
            offset = AlignDownToSector(offset);
            if (offset < 0 || offset >= diskSize)
                return;

            if (seen.Add(offset))
                offsets.Add(offset);
        }

        Add(0);
        Add(63L * SECTOR_SIZE);
        Add(128L * SECTOR_SIZE);
        Add(MiB);
        Add(16L * MiB);
        Add(32L * MiB);
        Add(64L * MiB);
        Add(128L * MiB);
        Add(256L * MiB);
        Add(512L * MiB);
        Add(GiB);

        for (long offset = MiB; offset > 0 && offset < diskSize; offset += MiB)
            Add(offset);

        offsets.Sort();
        return offsets;
    }

    public static List<CandidatePartition> CoalesceCandidates(IEnumerable<CandidatePartition> candidates) =>
        candidates
            .GroupBy(c => (c.Offset, FileSystem: c.FileSystem.ToUpperInvariant()))
            .Select(group => group
                .OrderByDescending(c => c.Confidence)
                .ThenByDescending(c => c.EstimatedSize)
                .First())
            .OrderBy(c => c.Offset)
            .ThenBy(c => c.FileSystem, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static CandidatePartition? DetectCandidate(byte[] buffer, long offset) => CheckSignatures(buffer, offset);

    private static ScanWorkResult ScanDisk(int diskNumber, long diskSize, IActivityLog log,
        RecoveryScanOptions options, string? statePath, IProgress<double>? progress, CancellationToken ct)
    {
        var path = $"\\\\.\\PhysicalDrive{diskNumber}";
        var handle = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_NO_BUFFERING, IntPtr.Zero);

        if (handle == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot open disk {diskNumber} for read-only scan");

        try
        {
            return options.Mode == RecoveryScanMode.Deep
                ? ScanDeep(handle, diskNumber, diskSize, log, options, statePath, progress, ct)
                : ScanFast(handle, diskSize, log, progress, ct);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static ScanWorkResult ScanFast(IntPtr handle, long diskSize,
        IActivityLog log, IProgress<double>? progress, CancellationToken ct)
    {
        var offsets = BuildFastProbeOffsets(diskSize);
        var candidates = new List<CandidatePartition>();
        var buffer = new byte[BUFFER_SIZE];
        long checkedOffsets = 0;
        long lastScannedOffset = 0;

        for (int i = 0; i < offsets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var offset = offsets[i];
            lastScannedOffset = offset;

            if (TryReadAt(handle, offset, buffer, out var bytesRead) && bytesRead >= SECTOR_SIZE)
            {
                checkedOffsets++;
                AddCandidateIfFound(candidates, buffer, offset, log);
            }

            if (i % 2048 == 0)
                progress?.Report(offsets.Count > 0 ? (double)(i + 1) / offsets.Count * 100 : 100);
        }

        progress?.Report(100);
        return new ScanWorkResult
        {
            Candidates = CoalesceCandidates(candidates),
            CheckedOffsetCount = checkedOffsets,
            CoverageBytes = Math.Min(diskSize, checkedOffsets * SECTOR_SIZE),
            LastScannedOffset = lastScannedOffset,
            IsComplete = true
        };
    }

    private static ScanWorkResult ScanDeep(IntPtr handle, int diskNumber, long diskSize,
        IActivityLog log, RecoveryScanOptions options, string? statePath, IProgress<double>? progress, CancellationToken ct)
    {
        var state = LoadResumeState(statePath, diskNumber, diskSize, log);
        var candidates = state?.Candidates ?? new List<CandidatePartition>();
        var buffer = new byte[BUFFER_SIZE];
        var offset = Math.Max(0, state?.NextOffset ?? 0);
        var checkedOffsets = state?.CheckedOffsetCount ?? 0;
        var lastCheckpointOffset = offset;
        var checkpointInterval = Math.Max(SECTOR_SIZE, options.CheckpointIntervalBytes);
        long lastScannedOffset = offset;

        if (state is not null)
            log.Log($"Resuming deep recovery scan from {SizeUtil.Format(offset)} using {statePath}");

        try
        {
            while (offset < diskSize)
            {
                ct.ThrowIfCancellationRequested();
                lastScannedOffset = offset;

                if (!TryReadAt(handle, offset, buffer, out var bytesRead) || bytesRead < SECTOR_SIZE)
                {
                    log.Log($"Read stopped at {SizeUtil.Format(offset)}; resume state saved for follow-up.");
                    SaveResumeState(statePath, diskNumber, diskSize, offset, checkedOffsets, candidates);
                    return new ScanWorkResult
                    {
                        Candidates = CoalesceCandidates(candidates),
                        CheckedOffsetCount = checkedOffsets,
                        CoverageBytes = Math.Min(diskSize, offset),
                        LastScannedOffset = lastScannedOffset,
                        IsComplete = false
                    };
                }

                checkedOffsets++;
                AddCandidateIfFound(candidates, buffer, offset, log);

                offset += SECTOR_SIZE;

                if (offset - lastCheckpointOffset >= checkpointInterval)
                {
                    progress?.Report((double)offset / diskSize * 100);
                    SaveResumeState(statePath, diskNumber, diskSize, offset, checkedOffsets, candidates);
                    lastCheckpointOffset = offset;
                }
            }
        }
        catch (OperationCanceledException)
        {
            SaveResumeState(statePath, diskNumber, diskSize, offset, checkedOffsets, candidates);
            throw;
        }

        progress?.Report(100);
        return new ScanWorkResult
        {
            Candidates = CoalesceCandidates(candidates),
            CheckedOffsetCount = checkedOffsets,
            CoverageBytes = diskSize,
            LastScannedOffset = Math.Max(0, diskSize - SECTOR_SIZE),
            IsComplete = true
        };
    }

    private static bool TryReadAt(IntPtr handle, long offset, byte[] buffer, out int bytesRead)
    {
        Array.Clear(buffer);
        bytesRead = 0;

        return SetFilePointerEx(handle, offset, out _, 0) &&
               ReadFile(handle, buffer, BUFFER_SIZE, out bytesRead, IntPtr.Zero);
    }

    private static void AddCandidateIfFound(List<CandidatePartition> candidates, byte[] buffer, long offset, IActivityLog log)
    {
        var candidate = CheckSignatures(buffer, offset);
        if (candidate is null)
            return;

        candidates.Add(candidate);
        log.Log($"  Found {candidate.FileSystem} signature at offset {SizeUtil.Format(offset)} ({candidate.Confidence:F0}% confidence)");
    }

    private static RecoveryScanResumeState? LoadResumeState(string? path, int diskNumber, long diskSize, IActivityLog log)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            var state = JsonSerializer.Deserialize<RecoveryScanResumeState>(File.ReadAllText(path), JsonOpts);
            if (state is null ||
                state.DiskNumber != diskNumber ||
                state.DiskSize != diskSize ||
                !string.Equals(state.ScanMode, RecoveryScanMode.Deep.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                log.Log($"Ignoring incompatible recovery resume state: {path}");
                return null;
            }

            state.Candidates = CoalesceCandidates(state.Candidates);
            return state;
        }
        catch (Exception ex)
        {
            log.Log($"Ignoring unreadable recovery resume state {path}: {ex.Message}");
            return null;
        }
    }

    private static void SaveResumeState(string? path, int diskNumber, long diskSize, long nextOffset,
        long checkedOffsetCount, List<CandidatePartition> candidates)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var state = new RecoveryScanResumeState
        {
            DiskNumber = diskNumber,
            DiskSize = diskSize,
            ScanMode = RecoveryScanMode.Deep.ToString(),
            NextOffset = nextOffset,
            CheckedOffsetCount = checkedOffsetCount,
            UpdatedAt = DateTimeOffset.UtcNow,
            Candidates = CoalesceCandidates(candidates)
        };
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOpts));
    }

    private static string ResolveResumeStatePath(int diskNumber, string? statePath) =>
        string.IsNullOrWhiteSpace(statePath) ? GetDefaultResumeStatePath(diskNumber) : statePath;

    private static long AlignDownToSector(long offset) => offset - offset % SECTOR_SIZE;

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
        sb.AppendLine($"Partition Recovery Scan - Disk {result.DiskNumber} ({SizeUtil.Format(result.DiskSize)})");
        sb.AppendLine($"Scanned at: {result.ScannedAt:yyyy-MM-dd HH:mm:ss} in {result.Duration.TotalSeconds:F1}s");
        sb.AppendLine($"Mode: {result.ScanMode}");
        sb.AppendLine($"Coverage: {SizeUtil.Format(result.CoverageBytes)} of {SizeUtil.Format(result.DiskSize)} ({result.CoveragePercent:F2}%) across {result.CheckedOffsetCount:N0} checked offset(s)");
        sb.AppendLine($"Status: {(result.IsComplete ? "Complete" : "Partial - resume state saved")}");
        if (!string.IsNullOrWhiteSpace(result.ResumeStatePath))
            sb.AppendLine($"Resume state: {result.ResumeStatePath}");
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
        JsonSerializer.Serialize(result, JsonOpts);

    private sealed class RecoveryScanResumeState
    {
        public int DiskNumber { get; set; }
        public long DiskSize { get; set; }
        public string ScanMode { get; set; } = RecoveryScanMode.Deep.ToString();
        public long NextOffset { get; set; }
        public long CheckedOffsetCount { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public List<CandidatePartition> Candidates { get; set; } = new();
    }

    private sealed class ScanWorkResult
    {
        public List<CandidatePartition> Candidates { get; init; } = new();
        public long CheckedOffsetCount { get; init; }
        public long CoverageBytes { get; init; }
        public long LastScannedOffset { get; init; }
        public bool IsComplete { get; init; }
    }
}
