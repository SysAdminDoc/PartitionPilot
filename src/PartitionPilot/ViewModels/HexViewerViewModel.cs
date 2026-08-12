using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32.SafeHandles;

namespace PartitionPilot;

public class HexViewerViewModel : ViewModelBase
{
    private readonly IWmiDiskService _wmiService;
    private readonly IActivityLog _log;

    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x01;
    private const uint FILE_SHARE_WRITE = 0x02;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurity, uint dwCreation, uint dwFlags, IntPtr hTemplate);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, int nBytes, out int bytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFilePointerEx(SafeFileHandle hFile, long distance, out long newPos, uint method);

    public System.Collections.ObjectModel.ObservableCollection<DiskInfo> Disks { get; } = new();

    private DiskInfo? _selectedDisk;
    public DiskInfo? SelectedDisk
    {
        get => _selectedDisk;
        set
        {
            if (SetProperty(ref _selectedDisk, value))
            {
                SectorOffset = 0;
                OnPropertyChanged(nameof(DiskSummary));
                OnPropertyChanged(nameof(SectorOffsetText));
                CommandManager.InvalidateRequerySuggested();
                if (value is not null)
                    _ = ReadSectorAsync();
            }
        }
    }

    private long _sectorOffset;
    public long SectorOffset
    {
        get => _sectorOffset;
        set
        {
            if (value < 0) value = 0;
            if (SetProperty(ref _sectorOffset, value))
                OnPropertyChanged(nameof(SectorOffsetText));
        }
    }

    public string SectorOffsetText
    {
        get
        {
            try
            {
                return $"LBA {SectorOffset} (offset {DiskGeometry.GetByteOffset(SectorOffset, LogicalSectorSize):N0})";
            }
            catch (OverflowException)
            {
                return $"LBA {SectorOffset} (offset exceeds supported range)";
            }
        }
    }

    private string _hexText = "";
    public string HexText
    {
        get => _hexText;
        set => SetProperty(ref _hexText, value);
    }

    private string _statusText = "Select a disk and read a sector.";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string DiskSummary => SelectedDisk is not null
        ? $"Disk {SelectedDisk.Number}: {SelectedDisk.FriendlyName} ({SizeUtil.Format(SelectedDisk.Size)})"
        : "";

    private int LogicalSectorSize => DiskGeometry.NormalizeLogicalSectorSize(SelectedDisk?.LogicalSectorSize ?? 0);

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public ICommand ReadSectorCommand { get; }
    public ICommand NextSectorCommand { get; }
    public ICommand PrevSectorCommand { get; }
    public ICommand GoToSectorCommand { get; }
    public ICommand RefreshCommand { get; }

    public HexViewerViewModel(IWmiDiskService wmiService, IActivityLog log)
    {
        _wmiService = wmiService;
        _log = log;

        ReadSectorCommand = new AsyncRelayCommand(_ => ReadSectorAsync(), _ => SelectedDisk is not null && !IsBusy);
        NextSectorCommand = new AsyncRelayCommand(_ => { SectorOffset++; return ReadSectorAsync(); }, _ => SelectedDisk is not null && !IsBusy);
        PrevSectorCommand = new AsyncRelayCommand(_ => { if (SectorOffset > 0) SectorOffset--; return ReadSectorAsync(); }, _ => SelectedDisk is not null && !IsBusy && SectorOffset > 0);
        GoToSectorCommand = new AsyncRelayCommand(_ => ReadSectorAsync(), _ => SelectedDisk is not null && !IsBusy);
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
    }

    public async Task RefreshAsync()
    {
        var disks = await _wmiService.GetDisksAsync();
        Application.Current.Dispatcher.Invoke(() =>
        {
            Disks.Clear();
            foreach (var d in disks) Disks.Add(d);
            if (Disks.Count > 0 && SelectedDisk is null)
                SelectedDisk = Disks[0];
        });
    }

    private async Task ReadSectorAsync()
    {
        var selectedDisk = SelectedDisk;
        if (selectedDisk is null) return;

        var sectorLba = SectorOffset;
        var logicalSectorSize = LogicalSectorSize;
        IsBusy = true;
        StatusText = $"Reading sector {sectorLba}...";

        try
        {
            var data = await Task.Run(() => ReadRawSector(selectedDisk.Number, sectorLba, logicalSectorSize));
            HexText = FormatHexDump(data, DiskGeometry.GetByteOffset(sectorLba, logicalSectorSize));
            StatusText = $"Sector {sectorLba} read ({data.Length} bytes)";
        }
        catch (Exception ex)
        {
            HexText = "";
            StatusText = $"Read failed: {ex.Message}";
            _log.Log($"Hex viewer read failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static byte[] ReadRawSector(int diskNumber, long sectorLba, int logicalSectorSize)
    {
        var path = $"\\\\.\\PhysicalDrive{diskNumber}";
        using var handle = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_NO_BUFFERING, IntPtr.Zero);

        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot open disk {diskNumber}");

        logicalSectorSize = DiskGeometry.NormalizeLogicalSectorSize(logicalSectorSize);
        long byteOffset = DiskGeometry.GetByteOffset(sectorLba, logicalSectorSize);
        int bufferAlignment = Math.Max(4096, logicalSectorSize);
        long alignedOffset = byteOffset / bufferAlignment * bufferAlignment;
        int offsetInBuffer = (int)(byteOffset - alignedOffset);

        var readBuffer = new byte[bufferAlignment];
        if (!SetFilePointerEx(handle, alignedOffset, out _, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Seek failed");

        if (!ReadFile(handle, readBuffer, readBuffer.Length, out int bytesRead, IntPtr.Zero) || bytesRead == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Read failed");

        if (bytesRead <= offsetInBuffer)
            throw new IOException("The disk read ended before the requested sector.");

        var result = new byte[Math.Min(logicalSectorSize, bytesRead - offsetInBuffer)];
        Buffer.BlockCopy(readBuffer, offsetInBuffer, result, 0, result.Length);
        return result;
    }

    private static string FormatHexDump(byte[] data, long baseOffset)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < data.Length; i += 16)
        {
            sb.Append($"{baseOffset + i:X8}  ");

            for (int j = 0; j < 16; j++)
            {
                if (i + j < data.Length)
                    sb.Append($"{data[i + j]:X2} ");
                else
                    sb.Append("   ");
                if (j == 7) sb.Append(' ');
            }

            sb.Append(" |");
            for (int j = 0; j < 16 && i + j < data.Length; j++)
            {
                var b = data[i + j];
                sb.Append(b is >= 32 and < 127 ? (char)b : '.');
            }
            sb.AppendLine("|");
        }
        return sb.ToString();
    }
}
