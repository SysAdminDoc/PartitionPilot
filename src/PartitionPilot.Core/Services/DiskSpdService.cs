using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace PartitionPilot;

public static class DiskSpdService
{
    private const string DiskSpdUrl = "https://github.com/microsoft/diskspd/releases/download/v2.2/DiskSpd.zip";
    private const string DiskSpdZipSha256 = "496DF11E6375C1D564AF3F8F2990734D9AFC2B558469FD57B1BFFA9313A5A6CE";
    private const string DiskSpdExeSha256 = "8F3B2F0909549C54253EDEE26C9A8D239B8B6C817B076BCD7EFB1BDA6571AEE9";

    private static string DiskSpdDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "PartitionPilot", "tools");

    private static string DiskSpdExe => Path.Combine(DiskSpdDir, "diskspd.exe");

    public static bool IsAvailable => HasExpectedFileHash(DiskSpdExe, DiskSpdExeSha256);

    public static async Task EnsureAvailableAsync(IActivityLog log, CancellationToken ct)
    {
        if (IsAvailable) return;
        if (File.Exists(DiskSpdExe))
            log.Log("Existing DiskSpd executable did not match the expected hash. Reinstalling...");

        log.Log("Downloading verified DiskSpd release from GitHub...");
        Directory.CreateDirectory(DiskSpdDir);

        var zipPath = Path.Combine(DiskSpdDir, "diskspd.zip");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var bytes = await client.GetByteArrayAsync(DiskSpdUrl, ct);
            var actualHash = ComputeSha256Hex(bytes);
            if (!string.Equals(actualHash, DiskSpdZipSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"DiskSpd download hash mismatch. Expected {DiskSpdZipSha256}, got {actualHash}.");

            await File.WriteAllBytesAsync(zipPath, bytes, ct);

            using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
            var entry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Contains("amd64", StringComparison.OrdinalIgnoreCase) &&
                e.Name.Equals("diskspd.exe", StringComparison.OrdinalIgnoreCase));

            if (entry is null)
                throw new FileNotFoundException("diskspd.exe not found in the downloaded archive.");

            entry.ExtractToFile(DiskSpdExe, overwrite: true);
            if (!HasExpectedFileHash(DiskSpdExe, DiskSpdExeSha256))
                throw new InvalidOperationException("Extracted DiskSpd executable did not match the expected hash.");

            log.Log($"DiskSpd installed to: {DiskSpdExe}");
        }
        finally
        {
            try { File.Delete(zipPath); } catch { }
        }
    }

    public record DiskSpdProfile(string Name, string Args);

    public static readonly DiskSpdProfile[] StandardProfiles =
    [
        new("SEQ1M Q1T1 Read",  "-b1M -o1 -t1 -W5 -d5 -Sh -w0 -Rxml"),
        new("SEQ1M Q1T1 Write", "-b1M -o1 -t1 -W5 -d5 -Sh -w100 -Rxml"),
        new("SEQ1M Q8T1 Read",  "-b1M -o8 -t1 -W5 -d5 -Sh -w0 -Rxml"),
        new("SEQ1M Q8T1 Write", "-b1M -o8 -t1 -W5 -d5 -Sh -w100 -Rxml"),
        new("RND4K Q1T1 Read",  "-b4K -o1 -t1 -r -W5 -d5 -Sh -w0 -Rxml"),
        new("RND4K Q1T1 Write", "-b4K -o1 -t1 -r -W5 -d5 -Sh -w100 -Rxml"),
        new("RND4K Q32T1 Read", "-b4K -o32 -t1 -r -W5 -d5 -Sh -w0 -Rxml"),
        new("RND4K Q32T1 Write","-b4K -o32 -t1 -r -W5 -d5 -Sh -w100 -Rxml"),
    ];

    public static async Task<BenchmarkResult> RunProfilesAsync(
        char driveLetter, IActivityLog log, IProgress<string> progress, CancellationToken ct)
    {
        var testFile = Path.Combine($"{driveLetter}:\\", $"pp_diskspd_{Guid.NewGuid():N}.dat");
        var result = new BenchmarkResult();
        var output = new StringBuilder();

        try
        {
            progress.Report("Preparing 1 GiB test file...");
            var successfulProfiles = 0;

            foreach (var profile in StandardProfiles)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report($"{profile.Name}...\n{output}");
                log.Log($"DiskSpd: {profile.Name}");

                var psi = new ProcessStartInfo
                {
                    FileName = DiskSpdExe,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                foreach (var arg in BuildProfileArguments(profile, testFile))
                    psi.ArgumentList.Add(arg);

                using var process = new Process { StartInfo = psi };
                process.Start();

                await using var reg = ct.Register(() =>
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                });

                var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
                var stderrTask = process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);
                var xmlOutput = await stdoutTask;
                var stderr = await stderrTask;

                if (process.ExitCode != 0)
                {
                    var detail = string.IsNullOrWhiteSpace(stderr) ? xmlOutput.Trim() : stderr.Trim();
                    log.Log($"DiskSpd {profile.Name} exited with code {process.ExitCode}: {detail}");
                    continue;
                }

                successfulProfiles++;
                var (mbps, iops) = ParseDiskSpdXml(xmlOutput);
                output.AppendLine($"{profile.Name,-22} {mbps,10:F1} MB/s  {iops,10:F0} IOPS");

                MapProfileResult(result, profile.Name, mbps, iops);
            }

            if (successfulProfiles == 0)
                throw new InvalidOperationException("DiskSpd did not complete any benchmark profile.");

            output.AppendLine();
            output.AppendLine("DiskSpd benchmark complete.");
            progress.Report(output.ToString());
            return result;
        }
        finally
        {
            try { File.Delete(testFile); } catch { }
        }
    }

    public static IReadOnlyList<string> BuildProfileArguments(DiskSpdProfile profile, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("A benchmark target path is required.", nameof(targetPath));

        var args = new List<string> { "-c1G" };
        args.AddRange(profile.Args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        args.Add(targetPath);
        return args;
    }

    public static string ComputeSha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string ComputeSha256Hex(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));

    private static bool HasExpectedFileHash(string path, string expectedHash)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            using var stream = File.OpenRead(path);
            return string.Equals(ComputeSha256Hex(stream), expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static (double mbps, double iops) ParseDiskSpdXml(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var timeSpan = doc.Descendants("TimeSpan").FirstOrDefault();
            if (timeSpan is null) return (0, 0);

            double totalBytes = 0;
            double totalIos = 0;

            foreach (var thread in timeSpan.Descendants("Thread"))
            {
                foreach (var target in thread.Descendants("Target"))
                {
                    var bytesCount = (double?)target.Element("BytesCount") ?? 0;
                    var ioCount = (double?)target.Element("IOCount") ?? 0;
                    totalBytes += bytesCount;
                    totalIos += ioCount;
                }
            }

            var testDuration = (double?)timeSpan.Element("TestTimeSeconds") ?? 5;
            var mbps = totalBytes / (1024 * 1024) / testDuration;
            var iops = totalIos / testDuration;
            return (mbps, iops);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static void MapProfileResult(BenchmarkResult result, string profileName, double mbps, double iops)
    {
        if (profileName.Contains("SEQ1M") && profileName.Contains("Q1T1"))
        {
            if (profileName.Contains("Read")) result.SeqReadMBps = mbps;
            else result.SeqWriteMBps = mbps;
        }
        else if (profileName.Contains("RND4K") && profileName.Contains("Q1T1"))
        {
            if (profileName.Contains("Read")) { result.Rnd4kReadMBps = mbps; result.Rnd4kReadIOPS = iops; }
            else { result.Rnd4kWriteMBps = mbps; result.Rnd4kWriteIOPS = iops; }
        }
    }
}
