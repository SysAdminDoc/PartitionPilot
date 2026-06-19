using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PartitionPilot;

public class SectorCloneProgress
{
    public long BytesCopied { get; set; }
    public long TotalBytes { get; set; }
    public double PercentComplete => TotalBytes > 0 ? (double)BytesCopied / TotalBytes * 100 : 0;
    public double BytesPerSecond { get; set; }
    public TimeSpan Elapsed { get; set; }
    public TimeSpan EstimatedRemaining { get; set; }

    public string RateText => BytesPerSecond > 0 ? $"{SizeUtil.Format((long)BytesPerSecond)}/s" : "";
    public string ProgressText => $"{SizeUtil.Format(BytesCopied)} / {SizeUtil.Format(TotalBytes)} ({PercentComplete:F1}%)";
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
    private static extern IntPtr CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        IntPtr hFile, byte[] lpBuffer, int nNumberOfBytesToRead,
        out int lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        IntPtr hFile, byte[] lpBuffer, int nNumberOfBytesToWrite,
        out int lpNumberOfBytesWritten, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice, uint dwIoControlCode,
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

    public static async Task CloneAsync(int sourceDiskNumber, int destDiskNumber, long sourceSize,
        IActivityLog log, IProgress<SectorCloneProgress>? progress = null, CancellationToken ct = default)
    {
        var sourcePath = $"\\\\.\\PhysicalDrive{sourceDiskNumber}";
        var destPath = $"\\\\.\\PhysicalDrive{destDiskNumber}";

        log.Log($"Starting sector clone: Disk {sourceDiskNumber} -> Disk {destDiskNumber} ({SizeUtil.Format(sourceSize)})");

        var sourceHandle = CreateFileW(sourcePath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_NO_BUFFERING, IntPtr.Zero);
        if (sourceHandle == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot open source disk {sourceDiskNumber}");

        var destHandle = CreateFileW(destPath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH, IntPtr.Zero);
        if (destHandle == new IntPtr(-1))
        {
            CloseHandle(sourceHandle);
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot open destination disk {destDiskNumber}");
        }

        try
        {
            DeviceIoControl(destHandle, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
            DeviceIoControl(destHandle, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);

            await Task.Run(() => CopyLoop(sourceHandle, destHandle, sourceSize, log, progress, ct), ct);

            log.Log($"Sector clone complete: {SizeUtil.Format(sourceSize)} copied from Disk {sourceDiskNumber} to Disk {destDiskNumber}");
        }
        finally
        {
            CloseHandle(destHandle);
            CloseHandle(sourceHandle);
        }
    }

    private static void CopyLoop(IntPtr source, IntPtr dest, long totalBytes,
        IActivityLog log, IProgress<SectorCloneProgress>? progress, CancellationToken ct)
    {
        var buffer = new byte[BUFFER_SIZE];
        long copied = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var lastReport = sw.Elapsed;

        while (copied < totalBytes)
        {
            ct.ThrowIfCancellationRequested();

            int toRead = (int)Math.Min(BUFFER_SIZE, totalBytes - copied);
            if (!ReadFile(source, buffer, toRead, out int bytesRead, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"Read failed at offset {copied} during sector clone");

            if (bytesRead == 0)
                throw new InvalidOperationException(
                    $"Source returned zero bytes at offset {copied} of {totalBytes} — clone incomplete");

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

            if (sw.Elapsed - lastReport > TimeSpan.FromSeconds(1))
            {
                var rate = copied / sw.Elapsed.TotalSeconds;
                var remaining2 = rate > 0 ? TimeSpan.FromSeconds((totalBytes - copied) / rate) : TimeSpan.Zero;
                progress?.Report(new SectorCloneProgress
                {
                    BytesCopied = copied,
                    TotalBytes = totalBytes,
                    BytesPerSecond = rate,
                    Elapsed = sw.Elapsed,
                    EstimatedRemaining = remaining2
                });
                lastReport = sw.Elapsed;
            }
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
            EstimatedRemaining = TimeSpan.Zero
        });
    }
}
