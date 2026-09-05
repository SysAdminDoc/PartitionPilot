using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PartitionPilot;

public static class NvmeHealthService
{
    /// <summary>
    /// No access rights at all. <c>IOCTL_STORAGE_QUERY_PROPERTY</c> is defined with <c>FILE_ANY_ACCESS</c>,
    /// so a handle opened this way succeeds without Administrator. Asking for GENERIC_READ would make the
    /// whole Disk Health tab unavailable in the app's unelevated read-only mode for no benefit.
    /// </summary>
    private const uint NO_ACCESS = 0;
    private const uint FILE_SHARE_READ = 0x01;
    private const uint FILE_SHARE_WRITE = 0x02;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    private const int StorageDeviceProtocolSpecificProperty = 50;
    private const int ProtocolTypeNvme = 3;
    private const int NVMeDataTypeLogPage = 2;
    private const int NVME_LOG_PAGE_HEALTH_INFO = 2;

    /// <summary>Largest NVMe log page, per Microsoft's own sample. The request buffer is sized for it.</summary>
    private const int NVME_MAX_LOG_SIZE = 4096;

    private const int HealthLogLength = 512;

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
    /// Reads NVMe SMART / health log page 02h into <paramref name="data"/>.
    /// Returns true when the log was read and parsed, so callers can use this as a standalone source
    /// rather than only as a top-up for data another provider already produced.
    /// </summary>
    public static bool EnrichSmartData(SmartData data, int diskNumber, IActivityLog? log = null)
    {
        var path = $"\\\\.\\PhysicalDrive{diskNumber}";
        using var handle = CreateFileW(path, NO_ACCESS, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            log?.Log($"NVMe health query: cannot open PhysicalDrive{diskNumber}");
            return false;
        }

        var querySize = Marshal.SizeOf<STORAGE_PROPERTY_QUERY>();
        var protocolDataSize = Marshal.SizeOf<STORAGE_PROTOCOL_SPECIFIC_DATA>();
        var bufferSize = querySize + NVME_MAX_LOG_SIZE;
        var buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            for (int i = 0; i < bufferSize; i++)
                Marshal.WriteByte(buffer, i, 0);

            var query = new STORAGE_PROPERTY_QUERY
            {
                PropertyId = StorageDeviceProtocolSpecificProperty,
                QueryType = 0,
                ProtocolSpecific = new STORAGE_PROTOCOL_SPECIFIC_DATA
                {
                    ProtocolType = ProtocolTypeNvme,
                    DataType = NVMeDataTypeLogPage,
                    ProtocolDataRequestValue = NVME_LOG_PAGE_HEALTH_INFO,
                    // Offset is measured from the start of STORAGE_PROTOCOL_SPECIFIC_DATA, not from the
                    // start of the query. The two happen to be equal here, but the contract is the former.
                    ProtocolDataOffset = protocolDataSize,
                    ProtocolDataLength = HealthLogLength
                }
            };

            Marshal.StructureToPtr(query, buffer, false);

            if (!DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY,
                buffer, bufferSize, buffer, bufferSize, out int bytesReturned, IntPtr.Zero))
            {
                log?.Log($"NVMe health IOCTL failed for drive {diskNumber} (not an NVMe device or the driver rejected the request)");
                return false;
            }

            // The reply is a STORAGE_PROTOCOL_DATA_DESCRIPTOR whose own Size field reports the header
            // length, not the payload length. Locate the log through the descriptor's offset and length
            // rather than assuming it starts where the request happened to put it.
            if (!TryLocateHealthLog(buffer, bytesReturned, protocolDataSize, out var payloadOffset, out var payloadLength))
            {
                log?.Log($"NVMe health IOCTL returned {bytesReturned} byte(s) for drive {diskNumber} " +
                         "without a usable health log descriptor");
                return false;
            }

            var healthData = new byte[HealthLogLength];
            Marshal.Copy(buffer + payloadOffset, healthData, 0, Math.Min(payloadLength, HealthLogLength));
            ParseHealthLog(data, healthData);
            log?.Log($"NVMe health log parsed for drive {diskNumber}");
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Reads the returned <c>STORAGE_PROTOCOL_DATA_DESCRIPTOR</c> to find where the health log actually
    /// landed. Its <c>ProtocolDataOffset</c> is relative to the start of the embedded
    /// <c>STORAGE_PROTOCOL_SPECIFIC_DATA</c>, which sits after the descriptor's 8-byte Version/Size pair.
    /// </summary>
    internal static bool TryLocateHealthLog(
        IntPtr buffer, int bytesReturned, int protocolDataSize, out int payloadOffset, out int payloadLength)
    {
        payloadOffset = 0;
        payloadLength = 0;

        const int descriptorHeaderSize = 8; // Version (4) + Size (4)
        if (bytesReturned < descriptorHeaderSize + protocolDataSize)
            return false;

        var specific = Marshal.PtrToStructure<STORAGE_PROTOCOL_SPECIFIC_DATA>(buffer + descriptorHeaderSize);
        var offset = descriptorHeaderSize + specific.ProtocolDataOffset;
        var length = specific.ProtocolDataLength;

        if (specific.ProtocolDataOffset < protocolDataSize || length <= 0)
            return false;
        if (offset < 0 || length > bytesReturned - offset)
            return false;

        payloadOffset = offset;
        payloadLength = length;
        return true;
    }

    private static void ParseHealthLog(SmartData data, byte[] log)
    {
        data.NvmeCriticalWarning = log[0];

        int tempKelvin = BitConverter.ToUInt16(log, 1);
        if (tempKelvin > 0)
            data.Temperature ??= tempKelvin - 273;

        data.NvmeAvailableSpare ??= log[3];

        int percentUsed = log[5];
        if (percentUsed > 0)
            data.Wear ??= percentUsed;

        data.PowerCycleCount ??= ReadUInt128AsLong(log, 0x70);
        data.PowerOnHours ??= ReadUInt128AsLong(log, 0x80);
        data.NvmeUnsafeShutdowns = ReadUInt128AsLong(log, 0x90);
        data.NvmeMediaErrors = ReadUInt128AsLong(log, 0xA0);
        data.NvmeErrorLogEntries = ReadUInt128AsLong(log, 0xB0);
        data.NvmeControllerBusyMinutes = ReadUInt128AsLong(log, 0x60);

        long dataUnitsWritten = ReadUInt128AsLong(log, 0x30);
        if (dataUnitsWritten > 0)
            data.TotalBytesWritten ??= dataUnitsWritten * 512 * 1000;

        long dataUnitsRead = ReadUInt128AsLong(log, 0x20);
        if (dataUnitsRead > 0)
            data.TotalBytesRead ??= dataUnitsRead * 512 * 1000;
    }

    private static long ReadUInt128AsLong(byte[] data, int offset)
    {
        if (offset + 8 > data.Length) return 0;
        var value = (long)BitConverter.ToUInt64(data, offset);
        return value < 0 ? long.MaxValue : value;
    }
}
