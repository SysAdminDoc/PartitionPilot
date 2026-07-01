using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PartitionPilot;

public static class NvmeHealthService
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x01;
    private const uint FILE_SHARE_WRITE = 0x02;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    private const int StorageDeviceProtocolSpecificProperty = 50;
    private const int ProtocolTypeNvme = 3;
    private const int NVMeDataTypeLogPage = 2;
    private const int NVME_LOG_PAGE_HEALTH_INFO = 2;

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

    public static void EnrichSmartData(SmartData data, int diskNumber, IActivityLog? log = null)
    {
        var path = $"\\\\.\\PhysicalDrive{diskNumber}";
        using var handle = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            log?.Log($"NVMe health query: cannot open PhysicalDrive{diskNumber}");
            return;
        }

        var querySize = Marshal.SizeOf<STORAGE_PROPERTY_QUERY>();
        int bufferSize = querySize + 512;
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
                    ProtocolDataOffset = querySize,
                    ProtocolDataLength = 512
                }
            };

            Marshal.StructureToPtr(query, buffer, false);

            if (!DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY,
                buffer, bufferSize, buffer, bufferSize, out int bytesReturned, IntPtr.Zero))
            {
                log?.Log($"NVMe health IOCTL failed for drive {diskNumber} (not NVMe or access denied)");
                return;
            }

            if (bytesReturned < querySize + 512)
            {
                log?.Log($"NVMe health IOCTL returned insufficient data ({bytesReturned} bytes) for drive {diskNumber}");
                return;
            }

            var healthData = new byte[512];
            Marshal.Copy(buffer + querySize, healthData, 0, 512);
            ParseHealthLog(data, healthData);
            log?.Log($"NVMe health log parsed for drive {diskNumber}");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
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
        return BitConverter.ToInt64(data, offset);
    }
}
