using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace PartitionPilot;

public class SectorCloneProgress
{
    public long BytesCopied { get; set; }
    public long TotalBytes { get; set; }
    public double PercentComplete => TotalBytes > 0 ? (double)BytesCopied / TotalBytes * 100 : 0;
    public double BytesPerSecond { get; set; }
    public TimeSpan Elapsed { get; set; }
    public TimeSpan EstimatedRemaining { get; set; }
    public string Phase { get; set; } = "Copying";

    public string RateText => BytesPerSecond > 0 ? $"{SizeUtil.Format((long)BytesPerSecond)}/s" : "";
    public string ProgressText => $"{Phase}: {SizeUtil.Format(BytesCopied)} / {SizeUtil.Format(TotalBytes)} ({PercentComplete:F1}%)";
}

public class SectorCloneResult
{
    public long BytesCopied { get; set; }
    public long TotalBytes { get; set; }
    public TimeSpan CopyDuration { get; set; }
    public List<long> BadSectorOffsets { get; set; } = new();
    public bool VerificationPassed { get; set; }
    public int VerificationMismatches { get; set; }
    public TimeSpan VerifyDuration { get; set; }

    public bool HasBadSectors => BadSectorOffsets.Count > 0;

    public string FormatReport()
    {
        var lines = new List<string>
        {
            $"Clone: {SizeUtil.Format(BytesCopied)} of {SizeUtil.Format(TotalBytes)} in {CopyDuration:hh\\:mm\\:ss}"
        };

        if (HasBadSectors)
        {
            long totalBlocks = TotalBytes > 0 ? (TotalBytes + 1048575) / 1048576 : 1;
            lines.Add($"Bad sectors: {BadSectorOffsets.Count} ({BadSectorOffsets.Count * 100.0 / totalBlocks:F4}% of blocks)");
        }

        if (VerifyDuration > TimeSpan.Zero)
        {
            lines.Add(VerificationPassed
                ? $"Verification: PASSED in {VerifyDuration:hh\\:mm\\:ss}"
                : $"Verification: FAILED — {VerificationMismatches} mismatched block(s) in {VerifyDuration:hh\\:mm\\:ss}");
        }

        return string.Join("\n", lines);
    }
}

public static class SectorCloneService
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x01;
    private const uint FILE_SHARE_WRITE = 0x02;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
    private const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;
    private const uint FSCTL_LOCK_VOLUME = 0x00090018;
    private const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;
    private const int BUFFER_SIZE = 1024 * 1024;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle hFile, byte[] lpBuffer, int nNumberOfBytesToRead,
        out int lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        SafeFileHandle hFile, byte[] lpBuffer, int nNumberOfBytesToWrite,
        out int lpNumberOfBytesWritten, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    public static void ValidateClone(DiskInfo source, DiskInfo destination)
    {
        if (source.Number == destination.Number)
            throw new InvalidOperationException("Source and destination cannot be the same disk.");

        if (destination.Size < source.Size)
            throw new InvalidOperationException(
                $"Destination disk ({SizeUtil.Format(destination.Size)}) is smaller than source ({SizeUtil.Format(source.Size)}).");

        if (source.IsPooled)
            throw new InvalidOperationException("Cannot clone from a Storage Spaces pooled disk.");

        if (destination.IsPooled)
            throw new InvalidOperationException("Cannot clone to a Storage Spaces pooled disk.");
    }

    public static async Task<SectorCloneResult> CloneAsync(int sourceDiskNumber, int destDiskNumber, long sourceSize,
        IActivityLog log, IProgress<SectorCloneProgress>? progress = null, CancellationToken ct = default,
        bool rescue = false, bool verify = true)
    {
        var sourcePath = $"\\\\.\\PhysicalDrive{sourceDiskNumber}";
        var destPath = $"\\\\.\\PhysicalDrive{destDiskNumber}";
        var result = new SectorCloneResult { TotalBytes = sourceSize };

        log.Log($"Starting sector clone: Disk {sourceDiskNumber} -> Disk {destDiskNumber} ({SizeUtil.Format(sourceSize)}){(rescue ? " [rescue mode]" : "")}");

        using var sourceHandle = CreateFileW(sourcePath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_NO_BUFFERING, IntPtr.Zero);
        if (sourceHandle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot open source disk {sourceDiskNumber}");

        using var destHandle = CreateFileW(destPath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH, IntPtr.Zero);
        if (destHandle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot open destination disk {destDiskNumber}");

        DeviceIoControl(destHandle, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
        DeviceIoControl(destHandle, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);

        EraseTargetSignatures(destHandle, log);

        var copyResult = await Task.Run(() => CopyLoop(sourceHandle, destHandle, sourceSize, log, progress, ct, rescue), ct);
        result.BytesCopied = copyResult.BytesCopied;
        result.CopyDuration = copyResult.CopyDuration;
        result.BadSectorOffsets = copyResult.BadSectors;

        if (result.HasBadSectors)
            log.Log($"Sector clone completed with {result.BadSectorOffsets.Count} bad sector(s) zeroed");

        log.Log($"Sector clone complete: {SizeUtil.Format(result.BytesCopied)} copied from Disk {sourceDiskNumber} to Disk {destDiskNumber}");

        if (verify)
        {
            log.Log("Starting post-clone verification...");
            var verifyResult = await VerifyAsync(sourceDiskNumber, destDiskNumber, sourceSize, log, progress, ct);
            result.VerificationPassed = verifyResult.Passed;
            result.VerificationMismatches = verifyResult.Mismatches;
            result.VerifyDuration = verifyResult.Duration;

            if (verifyResult.Passed)
                log.Log($"Verification passed: all {SizeUtil.Format(sourceSize)} verified in {verifyResult.Duration:hh\\:mm\\:ss}");
            else
                log.Log($"Verification FAILED: {verifyResult.Mismatches} mismatched block(s) detected");
        }

        return result;
    }

    public static async Task<VerifyResult> VerifyAsync(int sourceDiskNumber, int destDiskNumber, long totalBytes,
        IActivityLog log, IProgress<SectorCloneProgress>? progress = null, CancellationToken ct = default)
    {
        var sourcePath = $"\\\\.\\PhysicalDrive{sourceDiskNumber}";
        var destPath = $"\\\\.\\PhysicalDrive{destDiskNumber}";

        using var sourceHandle = CreateFileW(sourcePath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_NO_BUFFERING, IntPtr.Zero);
        if (sourceHandle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot open source disk {sourceDiskNumber} for verification");

        using var destHandle = CreateFileW(destPath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_NO_BUFFERING, IntPtr.Zero);
        if (destHandle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot open destination disk {destDiskNumber} for verification");

        return await Task.Run(() => VerifyLoop(sourceHandle, destHandle, totalBytes, progress, ct), ct);
    }

    private static void EraseTargetSignatures(SafeFileHandle destHandle, IActivityLog log)
    {
        const int eraseSize = 65536;
        var zeroBlock = new byte[eraseSize];
        if (WriteFile(destHandle, zeroBlock, eraseSize, out int written, IntPtr.Zero) && written == eraseSize)
            log.Log("Erased first 64KB of destination to clear existing filesystem signatures");
        else
            log.Log("Warning: could not erase destination signatures (clone will overwrite anyway)");
    }

    private static (long BytesCopied, TimeSpan CopyDuration, List<long> BadSectors) CopyLoop(
        SafeFileHandle source, SafeFileHandle dest, long totalBytes,
        IActivityLog log, IProgress<SectorCloneProgress>? progress, CancellationToken ct, bool rescue)
    {
        var buffer = new byte[BUFFER_SIZE];
        var zeroBuffer = rescue ? new byte[BUFFER_SIZE] : null;
        long copied = 0;
        var badSectors = new List<long>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var lastReport = sw.Elapsed;

        while (copied < totalBytes)
        {
            ct.ThrowIfCancellationRequested();

            int toRead = (int)Math.Min(BUFFER_SIZE, totalBytes - copied);
            bool readOk = ReadFile(source, buffer, toRead, out int bytesRead, IntPtr.Zero);

            if (!readOk || bytesRead == 0)
            {
                if (!rescue)
                {
                    if (!readOk)
                        throw new Win32Exception(Marshal.GetLastWin32Error(),
                            $"Read failed at offset {copied} during sector clone");
                    throw new InvalidOperationException(
                        $"Source returned zero bytes at offset {copied} of {totalBytes} — clone incomplete");
                }

                badSectors.Add(copied);
                log.Log($"Bad sector at offset {copied} ({SizeUtil.Format(copied)}) — zeroing destination block");

                if (!WriteFile(dest, zeroBuffer!, toRead, out _, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        $"Write of zero-fill failed at offset {copied} during rescue clone");

                copied += toRead;
                ReportProgress(progress, copied, totalBytes, sw, ref lastReport, "Copying");
                continue;
            }

            int written = 0;
            while (written < bytesRead)
            {
                int remaining = bytesRead - written;
                bool ok;
                int bytesWritten;
                if (written == 0)
                {
                    ok = WriteFile(dest, buffer, remaining, out bytesWritten, IntPtr.Zero);
                }
                else
                {
                    var tail = new byte[remaining];
                    Buffer.BlockCopy(buffer, written, tail, 0, remaining);
                    ok = WriteFile(dest, tail, remaining, out bytesWritten, IntPtr.Zero);
                }

                if (!ok)
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        $"Write failed at offset {copied + written} during sector clone");
                if (bytesWritten == 0)
                    throw new InvalidOperationException(
                        $"Write returned zero bytes at offset {copied + written} — clone incomplete");
                written += bytesWritten;
            }

            copied += bytesRead;
            ReportProgress(progress, copied, totalBytes, sw, ref lastReport, "Copying");
        }

        if (copied != totalBytes)
            throw new InvalidOperationException(
                $"Sector clone incomplete: copied {copied} of {totalBytes} bytes");

        progress?.Report(new SectorCloneProgress
        {
            BytesCopied = copied,
            TotalBytes = totalBytes,
            BytesPerSecond = sw.Elapsed.TotalSeconds > 0 ? copied / sw.Elapsed.TotalSeconds : 0,
            Elapsed = sw.Elapsed,
            EstimatedRemaining = TimeSpan.Zero,
            Phase = "Copying"
        });

        return (copied, sw.Elapsed, badSectors);
    }

    private static VerifyResult VerifyLoop(SafeFileHandle source, SafeFileHandle dest, long totalBytes,
        IProgress<SectorCloneProgress>? progress, CancellationToken ct)
    {
        var sourceBuffer = new byte[BUFFER_SIZE];
        var destBuffer = new byte[BUFFER_SIZE];
        long verified = 0;
        int mismatches = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var lastReport = sw.Elapsed;

        while (verified < totalBytes)
        {
            ct.ThrowIfCancellationRequested();

            int toRead = (int)Math.Min(BUFFER_SIZE, totalBytes - verified);

            if (!ReadFile(source, sourceBuffer, toRead, out int srcRead, IntPtr.Zero) || srcRead == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"Source read failed during verification at offset {verified}");

            if (!ReadFile(dest, destBuffer, toRead, out int dstRead, IntPtr.Zero) || dstRead == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"Destination read failed during verification at offset {verified}");

            int compareLen = Math.Min(srcRead, dstRead);
            if (!sourceBuffer.AsSpan(0, compareLen).SequenceEqual(destBuffer.AsSpan(0, compareLen)))
                mismatches++;

            verified += compareLen;
            ReportProgress(progress, verified, totalBytes, sw, ref lastReport, "Verifying");
        }

        progress?.Report(new SectorCloneProgress
        {
            BytesCopied = verified,
            TotalBytes = totalBytes,
            BytesPerSecond = sw.Elapsed.TotalSeconds > 0 ? verified / sw.Elapsed.TotalSeconds : 0,
            Elapsed = sw.Elapsed,
            EstimatedRemaining = TimeSpan.Zero,
            Phase = "Verifying"
        });

        return new VerifyResult { Passed = mismatches == 0, Mismatches = mismatches, Duration = sw.Elapsed };
    }

    private static void ReportProgress(IProgress<SectorCloneProgress>? progress,
        long processed, long total, System.Diagnostics.Stopwatch sw, ref TimeSpan lastReport, string phase)
    {
        if (sw.Elapsed - lastReport <= TimeSpan.FromSeconds(1)) return;

        var rate = processed / sw.Elapsed.TotalSeconds;
        var remaining = rate > 0 ? TimeSpan.FromSeconds((total - processed) / rate) : TimeSpan.Zero;
        progress?.Report(new SectorCloneProgress
        {
            BytesCopied = processed,
            TotalBytes = total,
            BytesPerSecond = rate,
            Elapsed = sw.Elapsed,
            EstimatedRemaining = remaining,
            Phase = phase
        });
        lastReport = sw.Elapsed;
    }

    public class VerifyResult
    {
        public bool Passed { get; set; }
        public int Mismatches { get; set; }
        public TimeSpan Duration { get; set; }
    }
}
