using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PartitionPilot;

public static class SecureEraseService
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_STORAGE_REINITIALIZE_MEDIA = 0x002D1500;

    // STORAGE_SANITIZE_METHOD values
    private const uint SanitizeMethodBlockErase = 1;
    private const uint SanitizeMethodCryptoErase = 2;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        nint lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, nint hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        ref uint lpInBuffer, uint nInBufferSize,
        nint lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, nint lpOverlapped);

    public enum SanitizeMethod
    {
        BlockErase,
        CryptoErase
    }

    public static bool IsNvmeSanitizeSupported()
    {
        return Environment.OSVersion.Version.Build >= 22000;
    }

    public static bool CanSanitizeDisk(DiskInfo? disk, IEnumerable<PhysicalDiskInfo> physicalDisks, out string reason)
        => CanSanitizeDisk(disk, physicalDisks, IsNvmeSanitizeSupported(), out reason);

    public static bool CanSanitizeDisk(
        DiskInfo? disk,
        IEnumerable<PhysicalDiskInfo> physicalDisks,
        bool osSupportsNvmeSanitize,
        out string reason)
    {
        if (!osSupportsNvmeSanitize)
        {
            reason = "NVMe sanitize requires Windows 11 or later.";
            return false;
        }

        if (disk is null)
        {
            reason = "Select a disk before using NVMe sanitize.";
            return false;
        }

        var physicalDisk = FindPhysicalDisk(disk, physicalDisks);
        if (physicalDisk is null)
        {
            reason = $"Could not verify Disk {disk.Number} as an NVMe physical disk.";
            return false;
        }

        if (!physicalDisk.BusType.Equals("NVMe", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Disk {disk.Number} is {physicalDisk.BusType}, not NVMe.";
            return false;
        }

        reason = $"Disk {disk.Number} is an NVMe disk and can be preflighted for sanitize.";
        return true;
    }

    private static PhysicalDiskInfo? FindPhysicalDisk(DiskInfo disk, IEnumerable<PhysicalDiskInfo> physicalDisks)
    {
        var diskNumber = disk.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return physicalDisks.FirstOrDefault(p => p.DeviceId == diskNumber)
               ?? physicalDisks.FirstOrDefault(p =>
                   p.Size == disk.Size &&
                   p.FriendlyName.Equals(disk.FriendlyName, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task ExecuteMultiPassWipeAsync(int diskNumber, int passCount, ProcessRunner runner, IActivityLog log, CancellationToken ct)
    {
        var passes = passCount switch
        {
            7 => new[] { 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, -1 },
            _ => new[] { 0x00, 0xFF, -1 }
        };

        log.Log($"Starting DoD {passes.Length}-pass wipe on Disk {diskNumber}...");

        var clearCmd = $"Clear-Disk -Number {diskNumber} -RemoveData -RemoveOEM -Confirm:$false";
        await runner.RunPowerShellAsync(clearCmd, log, ct);

        var initCmd = $"Initialize-Disk -Number {diskNumber} -PartitionStyle GPT -Confirm:$false";
        await runner.RunPowerShellAsync(initCmd, log, ct);

        var partCmd = $"New-Partition -DiskNumber {diskNumber} -UseMaximumSize -AssignDriveLetter | Format-Volume -FileSystem NTFS -Confirm:$false";
        await runner.RunPowerShellAsync(partCmd, log, ct);

        var letterCmd = $"(Get-Partition -DiskNumber {diskNumber} | Where-Object {{ $_.DriveLetter }} | Select-Object -First 1).DriveLetter";
        var letterResult = (await runner.RunPowerShellAsync(letterCmd, log, ct)).Trim();

        if (string.IsNullOrEmpty(letterResult) || !char.IsLetter(letterResult[0]))
            throw new InvalidOperationException("Could not assign a drive letter for wipe passes.");

        var driveLetter = letterResult[0];
        var tempPath = $"{driveLetter}:\\pp_wipe_{Guid.NewGuid():N}.tmp";

        for (int i = 0; i < passes.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var passLabel = passes[i] == -1 ? "random" : $"0x{passes[i]:X2}";
            log.Log($"Pass {i + 1}/{passes.Length}: writing {passLabel}...");

            await Task.Run(() =>
            {
                const int blockSize = 1024 * 1024;
                var buffer = new byte[blockSize];
                if (passes[i] >= 0)
                    Array.Fill(buffer, (byte)passes[i]);
                else
                    Random.Shared.NextBytes(buffer);

                using var fs = new System.IO.FileStream(tempPath,
                    System.IO.FileMode.Create, System.IO.FileAccess.Write,
                    System.IO.FileShare.None, blockSize, System.IO.FileOptions.WriteThrough);

                long written = 0;
                var passSw = System.Diagnostics.Stopwatch.StartNew();
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if (passes[i] == -1)
                            Random.Shared.NextBytes(buffer);
                        fs.Write(buffer, 0, blockSize);
                        written += blockSize;

                        if (written % (100 * blockSize) == 0 && passSw.Elapsed.TotalSeconds > 5)
                        {
                            var rateMBps = written / (1024.0 * 1024.0) / passSw.Elapsed.TotalSeconds;
                            log.Log($"Pass {i + 1}/{passes.Length}: {SizeUtil.Format(written)} written ({rateMBps:F0} MB/s)");
                        }
                    }
                    catch (System.IO.IOException)
                    {
                        break;
                    }
                }

                log.Log($"Pass {i + 1} complete: {SizeUtil.Format(written)} written in {passSw.Elapsed.TotalSeconds:F0}s.");
            }, ct);

            try { System.IO.File.Delete(tempPath); } catch { }
        }

        await runner.RunPowerShellAsync(clearCmd, log, ct);
        log.Log($"DoD {passes.Length}-pass wipe complete on Disk {diskNumber}.");
    }

    public static void ExecuteNvmeSanitize(int diskNumber, SanitizeMethod method, IActivityLog? log = null)
    {
        var devicePath = $@"\\.\PhysicalDrive{diskNumber}";
        log?.Log($"Opening {devicePath} for NVMe sanitize ({method})...");

        using var handle = CreateFileW(devicePath,
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            0, OPEN_EXISTING, 0, 0);

        if (handle.IsInvalid)
        {
            var err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Cannot open {devicePath} for sanitize (Win32 error {err}). Ensure the app is running as administrator.");
        }

        uint sanitizeMethod = method == SanitizeMethod.CryptoErase
            ? SanitizeMethodCryptoErase
            : SanitizeMethodBlockErase;

        log?.Log($"Sending IOCTL_STORAGE_REINITIALIZE_MEDIA with method {method}...");

        if (!DeviceIoControl(handle, IOCTL_STORAGE_REINITIALIZE_MEDIA,
                ref sanitizeMethod, sizeof(uint),
                0, 0, out _, 0))
        {
            var err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"NVMe sanitize failed (Win32 error {err}). The drive may not support this sanitize method.");
        }

        log?.Log($"NVMe sanitize ({method}) completed on disk {diskNumber}.");
    }
}
