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

    public static void ExecuteNvmeSanitize(int diskNumber, SanitizeMethod method, ActivityLog? log = null)
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
