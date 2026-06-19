using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

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
    private static extern IntPtr CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice, uint dwIoControlCode,
        ref MFT_ENUM_DATA_V0 lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private sealed record MftEntry(string Name, long ParentRef, long Size, bool IsDirectory);

    public static List<FolderSizeInfo> ScanVolume(char driveLetter, int topN = 30, CancellationToken ct = default)
    {
        var volumePath = $"\\\\.\\{driveLetter}:";
        var handle = CreateFileW(volumePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);

        if (handle == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot open volume {driveLetter}:");

        try
        {
            var entries = EnumerateMft(handle, ct);
            return BuildTopFolders(entries, driveLetter, topN);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static Dictionary<long, MftEntry> EnumerateMft(IntPtr handle, CancellationToken ct)
    {
        var entries = new Dictionary<long, MftEntry>();
        const int bufferSize = 64 * 1024;
        var buffer = Marshal.AllocHGlobal(bufferSize);

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

                if (!success || bytesReturned <= 8)
                    break;

                enumData.StartFileReferenceNumber = Marshal.ReadInt64(buffer);

                int offset = 8;
                while (offset < bytesReturned)
                {
                    int recordLength = Marshal.ReadInt32(buffer, offset);
                    if (recordLength == 0) break;

                    long fileRef = Marshal.ReadInt64(buffer, offset + 8);
                    long parentRef = Marshal.ReadInt64(buffer, offset + 16);
                    int fileNameLength = Marshal.ReadInt16(buffer, offset + 56);
                    int fileNameOffset = Marshal.ReadInt16(buffer, offset + 58);
                    int attributes = Marshal.ReadInt32(buffer, offset + 52);

                    long maskedRef = fileRef & 0x0000FFFFFFFFFFFF;
                    long maskedParent = parentRef & 0x0000FFFFFFFFFFFF;
                    bool isDir = (attributes & 0x10) != 0;

                    string name = "";
                    if (fileNameLength > 0)
                    {
                        name = Marshal.PtrToStringUni(buffer + offset + fileNameOffset, fileNameLength / 2) ?? "";
                    }

                    if (!string.IsNullOrEmpty(name) && name != "." && name != "..")
                    {
                        long size = 0;
                        if (!isDir)
                        {
                            size = Marshal.ReadInt64(buffer, offset + 40);
                            if (size < 0) size = 0;
                        }

                        entries[maskedRef] = new MftEntry(name, maskedParent, size, isDir);
                    }

                    offset += recordLength;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return entries;
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

            var parentRef = entry.ParentRef;
            while (parentRef != rootRef && entries.TryGetValue(parentRef, out var parent))
            {
                parentRef = parent.ParentRef;
            }

            var topParent = entry.ParentRef;
            if (entries.TryGetValue(entry.ParentRef, out var directParent))
            {
                var ancestor = entry.ParentRef;
                while (ancestor != rootRef && entries.TryGetValue(ancestor, out var anc))
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
