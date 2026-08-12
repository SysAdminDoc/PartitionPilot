using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PartitionPilot;

public static class MftScanner
{
    private const uint FSCTL_ENUM_USN_DATA = 0x000900B3;
    private const uint FILE_SHARE_READ = 0x01;
    private const uint FILE_SHARE_WRITE = 0x02;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MFT_ENUM_DATA_V0
    {
        public long StartFileReferenceNumber;
        public long LowUsn;
        public long HighUsn;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        ref MFT_ENUM_DATA_V0 lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    private sealed record MftEntry(string Name, long ParentRef, long Size, bool IsDirectory);

    internal readonly record struct UsnRecord(
        int RecordLength,
        long FileReference,
        long ParentReference,
        long Size,
        bool IsDirectory,
        string Name);

    public static List<FolderSizeInfo> ScanVolume(char driveLetter, int topN = 30, CancellationToken ct = default)
    {
        var volumePath = $"\\\\.\\{driveLetter}:";
        using var handle = CreateFileW(volumePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);

        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot open volume {driveLetter}:");

        var entries = EnumerateMft(handle, ct);
        return BuildTopFolders(entries, driveLetter, topN);
    }

    private static Dictionary<long, MftEntry> EnumerateMft(SafeFileHandle handle, CancellationToken ct)
    {
        var entries = new Dictionary<long, MftEntry>();
        const int bufferSize = 64 * 1024;
        var buffer = Marshal.AllocHGlobal(bufferSize);
        var managedBuffer = new byte[bufferSize];

        try
        {
            var enumData = new MFT_ENUM_DATA_V0
            {
                StartFileReferenceNumber = 0,
                LowUsn = 0,
                HighUsn = long.MaxValue
            };

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool success = DeviceIoControl(handle, FSCTL_ENUM_USN_DATA,
                    ref enumData, Marshal.SizeOf<MFT_ENUM_DATA_V0>(),
                    buffer, bufferSize, out int bytesReturned, IntPtr.Zero);

                if (!success || bytesReturned <= 8 || bytesReturned > bufferSize)
                    break;

                enumData.StartFileReferenceNumber = Marshal.ReadInt64(buffer);
                Marshal.Copy(buffer, managedBuffer, 0, bytesReturned);

                int offset = 8;
                while (offset < bytesReturned)
                {
                    if (!TryParseUsnRecord(managedBuffer.AsSpan(offset, bytesReturned - offset), out var record))
                        break;

                    long maskedRef = record.FileReference & 0x0000FFFFFFFFFFFF;
                    long maskedParent = record.ParentReference & 0x0000FFFFFFFFFFFF;

                    if (!string.IsNullOrEmpty(record.Name) && record.Name != "." && record.Name != "..")
                    {
                        entries[maskedRef] = new MftEntry(record.Name, maskedParent, record.Size, record.IsDirectory);
                    }

                    offset += record.RecordLength;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return entries;
    }

    internal static bool TryParseUsnRecord(ReadOnlySpan<byte> data, out UsnRecord record)
    {
        record = default;
        const int minimumRecordLength = 60;

        if (data.Length < sizeof(int))
            return false;

        int recordLength = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (recordLength < minimumRecordLength || recordLength > data.Length)
            return false;

        var boundedRecord = data[..recordLength];
        int fileNameLength = BinaryPrimitives.ReadUInt16LittleEndian(boundedRecord[56..]);
        int fileNameOffset = BinaryPrimitives.ReadUInt16LittleEndian(boundedRecord[58..]);
        if ((fileNameLength & 1) != 0 ||
            fileNameOffset < minimumRecordLength ||
            fileNameOffset > recordLength ||
            fileNameLength > recordLength - fileNameOffset)
        {
            return false;
        }

        long fileReference = BinaryPrimitives.ReadInt64LittleEndian(boundedRecord[8..]);
        long parentReference = BinaryPrimitives.ReadInt64LittleEndian(boundedRecord[16..]);
        int attributes = BinaryPrimitives.ReadInt32LittleEndian(boundedRecord[52..]);
        bool isDirectory = (attributes & 0x10) != 0;
        long size = isDirectory ? 0 : Math.Max(0, BinaryPrimitives.ReadInt64LittleEndian(boundedRecord[40..]));
        string name = fileNameLength == 0
            ? ""
            : Encoding.Unicode.GetString(boundedRecord.Slice(fileNameOffset, fileNameLength));

        record = new UsnRecord(recordLength, fileReference, parentReference, size, isDirectory, name);
        return true;
    }

    private static List<FolderSizeInfo> BuildTopFolders(Dictionary<long, MftEntry> entries,
        char driveLetter, int topN)
    {
        const long rootRef = 5;
        var dirSizes = new Dictionary<long, long>();
        var dirCounts = new Dictionary<long, int>();

        foreach (var (refNum, entry) in entries)
        {
            if (entry.IsDirectory || entry.Size <= 0) continue;

            const int maxDepth = 256;
            var parentRef = entry.ParentRef;
            int depth = 0;
            while (parentRef != rootRef && entries.TryGetValue(parentRef, out var parent) && depth++ < maxDepth)
            {
                parentRef = parent.ParentRef;
            }

            var topParent = entry.ParentRef;
            if (entries.TryGetValue(entry.ParentRef, out var directParent))
            {
                var ancestor = entry.ParentRef;
                depth = 0;
                while (ancestor != rootRef && entries.TryGetValue(ancestor, out var anc) && depth++ < maxDepth)
                {
                    if (anc.ParentRef == rootRef)
                    {
                        topParent = ancestor;
                        break;
                    }
                    ancestor = anc.ParentRef;
                }
                if (ancestor == rootRef) topParent = entry.ParentRef;
            }

            if (!dirSizes.ContainsKey(topParent)) dirSizes[topParent] = 0;
            if (!dirCounts.ContainsKey(topParent)) dirCounts[topParent] = 0;
            dirSizes[topParent] += entry.Size;
            dirCounts[topParent]++;
        }

        long rootFileSize = 0;
        int rootFileCount = 0;
        foreach (var (_, entry) in entries)
        {
            if (!entry.IsDirectory && entry.Size > 0 && entry.ParentRef == rootRef)
            {
                rootFileSize += entry.Size;
                rootFileCount++;
            }
        }

        var results = new List<FolderSizeInfo>();
        foreach (var (dirRef, size) in dirSizes.OrderByDescending(kv => kv.Value).Take(topN))
        {
            if (entries.TryGetValue(dirRef, out var dirEntry))
            {
                results.Add(new FolderSizeInfo
                {
                    Path = $"{driveLetter}:\\{dirEntry.Name}",
                    Name = dirEntry.Name,
                    Size = size,
                    FileCount = dirCounts.GetValueOrDefault(dirRef)
                });
            }
        }

        if (rootFileSize > 0)
        {
            results.Add(new FolderSizeInfo
            {
                Path = $"{driveLetter}:\\",
                Name = "(root files)",
                Size = rootFileSize,
                FileCount = rootFileCount
            });
        }

        results.Sort((a, b) => b.Size.CompareTo(a.Size));
        return results.Take(topN).ToList();
    }
}
