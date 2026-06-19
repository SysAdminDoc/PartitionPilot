using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;

namespace PartitionPilot;

public static class DiskSpdService
{
    private const string DiskSpdUrl = "https://github.com/microsoft/diskspd/releases/download/v2.2/DiskSpd.zip";

    private static string DiskSpdDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "PartitionPilot", "tools");

    private static string DiskSpdExe => Path.Combine(DiskSpdDir, "diskspd.exe");

    public static bool IsAvailable => File.Exists(DiskSpdExe);

    public static async Task EnsureAvailableAsync(ActivityLog log, CancellationToken ct)
    {
        if (IsAvailable) return;

        log.Log("DiskSpd not found. Downloading from GitHub...");
        Directory.CreateDirectory(DiskSpdDir);

        var zipPath = Path.Combine(DiskSpdDir, "diskspd.zip");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var bytes = await client.GetByteArrayAsync(DiskSpdUrl, ct);
            await File.WriteAllBytesAsync(zipPath, bytes, ct);

            using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
            var entry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Contains("amd64", StringComparison.OrdinalIgnoreCase) &&
                e.Name.Equals("diskspd.exe", StringComparison.OrdinalIgnoreCase));

            if (entry is null)
                throw new FileNotFoundException("diskspd.exe not found in the downloaded archive.");

            entry.ExtractToFile(DiskSpdExe, overwrite: true);
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
        char driveLetter, ActivityLog log, IProgress<string> progress, CancellationToken ct)
    {
        var testFile = Path.Combine($"{driveLetter}:\\", $"pp_diskspd_{Guid.NewGuid():N}.dat");
        var result = new BenchmarkResult();
        var output = new StringBuilder();

        try
        {
            progress.Report("Preparing 1 GiB test file...");
            var createArgs = $"-c1G {testFile}";

            foreach (var profile in StandardProfiles)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report($"{profile.Name}...\n{output}");
                log.Log($"DiskSpd: {profile.Name}");

                var args = $"{profile.Args} {testFile}";
                var psi = new ProcessStartInfo
                {
                    FileName = DiskSpdExe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                await using var reg = ct.Register(() =>
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                });

                var xmlOutput = await process.StandardOutput.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);

                if (process.ExitCode != 0)
                {
                    log.Log($"DiskSpd {profile.Name} exited with code {process.ExitCode}");
                    continue;
                }

                var (mbps, iops) = ParseDiskSpdXml(xmlOutput);
                output.AppendLine($"{profile.Name,-22} {mbps,10:F1} MB/s  {iops,10:F0} IOPS");

                MapProfileResult(result, profile.Name, mbps, iops);
            }

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
