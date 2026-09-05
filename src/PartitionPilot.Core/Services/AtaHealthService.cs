using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PartitionPilot;

/// <summary>
/// Reads SATA drive health from the ATA Device Statistics log (GPL log 0x04) through
/// <c>IOCTL_STORAGE_QUERY_PROPERTY</c>.
/// <para>
/// This is the SATA counterpart to <see cref="NvmeHealthService"/>, and it exists for the same reason:
/// the IOCTL is defined with <c>FILE_ANY_ACCESS</c>, so it works from an unelevated handle, where
/// <c>MSFT_StorageReliabilityCounter</c> does not. Without it, SATA health depends entirely on a
/// third-party library that may report nothing.
/// </para>
/// <para>
/// Classic SMART attribute IDs still need <c>IOCTL_ATA_PASS_THROUGH</c> and elevation; Device Statistics
/// is the standardised, unprivileged subset.
/// </para>
/// </summary>
public static class AtaHealthService
{
    private const uint NO_ACCESS = 0;
    private const uint FILE_SHARE_READ = 0x01;
    private const uint FILE_SHARE_WRITE = 0x02;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    private const int StorageDeviceProtocolSpecificProperty = 50;
    private const int ProtocolTypeAta = 2;
    private const int AtaDataTypeLogPage = 2;
    private const int DeviceStatisticsLog = 0x04;
    private const int MaxLogSize = 4096;
    private const int LogPageLength = 512;

    // Device Statistics pages, per ACS-4 section 9.5.
    private const int GeneralStatisticsPage = 1;
    private const int TemperatureStatisticsPage = 5;
    private const int SolidStateDevicePage = 7;

    // Byte offsets of each statistic within its page. Every entry is an 8-byte little-endian field:
    // bits 0-55 carry the value, bit 63 marks it supported and bit 62 marks it valid.
    private const int PowerOnResetsOffset = 8;
    private const int PowerOnHoursOffset = 16;
    private const int LogicalSectorsWrittenOffset = 24;
    private const int LogicalSectorsReadOffset = 40;
    private const int CurrentTemperatureOffset = 8;
    private const int HighestTemperatureOffset = 32;
    private const int PercentageUsedEnduranceOffset = 8;

    private const byte SupportedFlag = 0x80;
    private const byte ValidFlag = 0x40;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PROPERTY_QUERY
    {
        public int PropertyId;
        public int QueryType;
        public STORAGE_PROTOCOL_SPECIFIC_DATA ProtocolSpecific;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PROTOCOL_SPECIFIC_DATA
    {
        public int ProtocolType;
        public int DataType;
        public int ProtocolDataRequestValue;
        public int ProtocolDataRequestSubValue;
        public int ProtocolDataOffset;
        public int ProtocolDataLength;
        public int FixedProtocolReturnData;
        public int ProtocolDataRequestSubValue2;
        public int ProtocolDataRequestSubValue3;
        public int ProtocolDataRequestSubValue4;
    }

    /// <summary>
    /// Fills <paramref name="data"/> from the drive's Device Statistics log.
    /// Returns true when at least one statistic was read.
    /// </summary>
    public static bool EnrichSmartData(
        SmartData data, int diskNumber, int logicalSectorSize = DiskGeometry.DefaultLogicalSectorSize,
        IActivityLog? log = null)
    {
        var sectorSize = DiskGeometry.NormalizeLogicalSectorSize(logicalSectorSize);

        var path = $"\\\\.\\PhysicalDrive{diskNumber}";
        using var handle = CreateFileW(path, NO_ACCESS, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            log?.Log($"ATA health query: cannot open PhysicalDrive{diskNumber}");
            return false;
        }

        var general = ReadPage(handle, GeneralStatisticsPage);
        var temperature = ReadPage(handle, TemperatureStatisticsPage);
        var solidState = ReadPage(handle, SolidStateDevicePage);

        if (general is null && temperature is null && solidState is null)
        {
            log?.Log($"ATA Device Statistics unavailable for drive {diskNumber} (not an ATA device or log not supported)");
            return false;
        }

        var read = ApplyStatistics(data, general, temperature, solidState, sectorSize);
        if (read)
            log?.Log($"ATA Device Statistics parsed for drive {diskNumber}");
        else
            log?.Log($"ATA Device Statistics for drive {diskNumber} reported no supported statistics");

        return read;
    }

    /// <summary>Maps already-read Device Statistics pages onto <paramref name="data"/>.</summary>
    internal static bool ApplyStatistics(
        SmartData data, byte[]? general, byte[]? temperature, byte[]? solidState, int logicalSectorSize)
    {
        var read = false;

        if (TryReadStatistic(general, PowerOnHoursOffset, out var hours))
        {
            data.PowerOnHours ??= hours;
            read = true;
        }

        if (TryReadStatistic(general, PowerOnResetsOffset, out var resets))
        {
            data.PowerCycleCount ??= resets;
            read = true;
        }

        if (TryReadStatistic(general, LogicalSectorsWrittenOffset, out var sectorsWritten))
        {
            data.TotalBytesWritten ??= SafeMultiply(sectorsWritten, logicalSectorSize);
            read = true;
        }

        if (TryReadStatistic(general, LogicalSectorsReadOffset, out var sectorsRead))
        {
            data.TotalBytesRead ??= SafeMultiply(sectorsRead, logicalSectorSize);
            read = true;
        }

        if (TryReadStatistic(temperature, CurrentTemperatureOffset, out var currentTemp) &&
            currentTemp is > -100 and < 200)
        {
            data.Temperature ??= (int)currentTemp;
            read = true;
        }

        if (TryReadStatistic(temperature, HighestTemperatureOffset, out var highestTemp) &&
            highestTemp is > -100 and < 200)
        {
            data.AtaHighestTemperature ??= (int)highestTemp;
            read = true;
        }

        if (TryReadStatistic(solidState, PercentageUsedEnduranceOffset, out var used) &&
            used is >= 0 and <= 255)
        {
            data.Wear ??= (int)used;
            read = true;
        }

        return read;
    }

    /// <summary>
    /// Reads one Device Statistics entry. Each is 8 bytes: a 56-bit value plus flag bits, and a statistic
    /// the drive does not support or has not populated must be ignored rather than reported as zero.
    /// </summary>
    internal static bool TryReadStatistic(byte[]? page, int offset, out long value)
    {
        value = 0;
        if (page is null || offset < 0 || offset + 8 > page.Length)
            return false;

        var flags = page[offset + 7];
        if ((flags & SupportedFlag) == 0 || (flags & ValidFlag) == 0)
            return false;

        long parsed = 0;
        for (var i = 6; i >= 0; i--)
            parsed = (parsed << 8) | page[offset + i];

        value = parsed;
        return true;
    }

    private static long SafeMultiply(long sectors, int sectorSize)
    {
        if (sectors <= 0) return 0;
        return sectors > long.MaxValue / sectorSize ? long.MaxValue : sectors * sectorSize;
    }

    private static byte[]? ReadPage(SafeFileHandle handle, int page)
    {
        var querySize = Marshal.SizeOf<STORAGE_PROPERTY_QUERY>();
        var protocolDataSize = Marshal.SizeOf<STORAGE_PROTOCOL_SPECIFIC_DATA>();
        var bufferSize = querySize + MaxLogSize;
        var buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            for (var i = 0; i < bufferSize; i++)
                Marshal.WriteByte(buffer, i, 0);

            var query = new STORAGE_PROPERTY_QUERY
            {
                PropertyId = StorageDeviceProtocolSpecificProperty,
                QueryType = 0,
                ProtocolSpecific = new STORAGE_PROTOCOL_SPECIFIC_DATA
                {
                    ProtocolType = ProtocolTypeAta,
                    DataType = AtaDataTypeLogPage,
                    ProtocolDataRequestValue = DeviceStatisticsLog,
                    ProtocolDataRequestSubValue = page,
                    ProtocolDataOffset = protocolDataSize,
                    ProtocolDataLength = LogPageLength
                }
            };

            Marshal.StructureToPtr(query, buffer, false);

            if (!DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY,
                    buffer, bufferSize, buffer, bufferSize, out var bytesReturned, IntPtr.Zero))
                return null;

            if (!NvmeHealthService.TryLocateHealthLog(buffer, bytesReturned, protocolDataSize, out var offset, out var length))
                return null;

            var payload = new byte[Math.Min(length, LogPageLength)];
            Marshal.Copy(buffer + offset, payload, 0, payload.Length);

            // A page the drive does not implement comes back as zeroes; its header would not name the page.
            return payload[2] == page ? payload : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
