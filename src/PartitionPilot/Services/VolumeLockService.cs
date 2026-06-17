using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PartitionPilot;

public static class VolumeLockService
{
    private const uint FSCTL_LOCK_VOLUME = 0x00090018;
    private const uint FSCTL_UNLOCK_VOLUME = 0x0009001C;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        nint lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, nint hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        nint lpInBuffer, uint nInBufferSize,
        nint lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, nint lpOverlapped);

    public static VolumeLock? TryLock(char driveLetter, ActivityLog? log = null)
    {
        driveLetter = char.ToUpperInvariant(driveLetter);
        var volumePath = $@"\\.\{driveLetter}:";

        var handle = CreateFileW(volumePath,
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            0, OPEN_EXISTING, 0, 0);

        if (handle.IsInvalid)
        {
            var err = Marshal.GetLastWin32Error();
            log?.Log($"Could not open volume {driveLetter}: for locking (Win32 error {err}). Proceeding without lock.");
            return null;
        }

        if (!DeviceIoControl(handle, FSCTL_LOCK_VOLUME, 0, 0, 0, 0, out _, 0))
        {
            var err = Marshal.GetLastWin32Error();
            log?.Log($"Could not lock volume {driveLetter}: (Win32 error {err}). Another process may be using it.");
            handle.Dispose();
            return null;
        }

        log?.Log($"Volume {driveLetter}: locked for exclusive access.");
        return new VolumeLock(handle, driveLetter, log);
    }
}

public sealed class VolumeLock : IDisposable
{
    private const uint FSCTL_UNLOCK_VOLUME = 0x0009001C;

    private readonly SafeFileHandle _handle;
    private readonly char _letter;
    private readonly ActivityLog? _log;
    private bool _disposed;

    internal VolumeLock(SafeFileHandle handle, char letter, ActivityLog? log)
    {
        _handle = handle;
        _letter = letter;
        _log = log;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DeviceIoControl(_handle, FSCTL_UNLOCK_VOLUME, 0, 0, 0, 0, out _, 0);
        _log?.Log($"Volume {_letter}: unlocked.");
        _handle.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        nint lpInBuffer, uint nInBufferSize,
        nint lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, nint lpOverlapped);
}
