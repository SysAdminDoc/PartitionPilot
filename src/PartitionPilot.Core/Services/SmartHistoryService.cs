using System.IO;
using System.Text.Json;

namespace PartitionPilot;

public class SmartHistoryService
{
    private static readonly string HistoryDir = ResolveHistoryDir();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private const int MaxReadingsPerDevice = 365;

    private static string ResolveHistoryDir()
    {
        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "portable.txt")))
            return Path.Combine(exeDir, "smart_history");
        var programData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PartitionPilot", "smart_history");
        try
        {
            Directory.CreateDirectory(programData);
            return programData;
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), "PartitionPilot", "smart_history");
        }
    }

    private const int CurrentSchemaVersion = 1;

    private sealed class SmartHistoryEnvelope
    {
        public int SchemaVersion { get; set; }
        public List<SmartReading> Readings { get; set; } = new();
    }

    public async Task RecordAsync(string deviceId, SmartData data)
    {
        var reading = SmartReading.FromSmartData(data);
        var readings = await LoadReadingsAsync(deviceId);
        readings.Add(reading);

        if (readings.Count > MaxReadingsPerDevice)
            readings.RemoveRange(0, readings.Count - MaxReadingsPerDevice);

        Directory.CreateDirectory(HistoryDir);
        var path = GetFilePath(deviceId);
        var envelope = new SmartHistoryEnvelope
        {
            SchemaVersion = CurrentSchemaVersion,
            Readings = readings
        };
        var json = JsonSerializer.Serialize(envelope, JsonOpts);
        await File.WriteAllTextAsync(path, json);
    }

    public async Task<List<SmartReading>> GetHistoryAsync(string deviceId)
    {
        return await LoadReadingsAsync(deviceId);
    }

    public static List<SmartTrend> AnalyzeTrends(List<SmartReading> readings)
    {
        var trends = new List<SmartTrend>();
        if (readings.Count < 2) return trends;

        var recent = readings.TakeLast(10).ToList();
        var oldest = recent.First();
        var newest = recent.Last();

        CheckMonotonicIncrease(trends, recent,
            r => r.ReallocatedSectors, "Reallocated Sectors",
            "sectors are being reallocated — surface degradation in progress", "Warning");

        CheckMonotonicIncrease(trends, recent,
            r => r.PendingSectors, "Pending Sectors",
            "pending sector count rising — reallocation queue growing", "Warning");

        CheckMonotonicIncrease(trends, recent,
            r => r.NvmeMediaErrors, "NVMe Media Errors",
            "media error count increasing — drive reliability declining", "Warning");

        if (oldest.Wear.HasValue && newest.Wear.HasValue)
        {
            var delta = newest.Wear.Value - oldest.Wear.Value;
            if (delta > 0)
            {
                var severity = newest.Wear.Value >= 85 ? "Critical" : newest.Wear.Value >= 70 ? "Warning" : "Info";
                trends.Add(new SmartTrend
                {
                    Attribute = "SSD Wear",
                    Direction = "Increasing",
                    Severity = severity,
                    Message = $"Wear increased {delta}% over last {recent.Count} readings (now {newest.Wear}%)"
                });
            }
        }

        if (oldest.NvmeAvailableSpare.HasValue && newest.NvmeAvailableSpare.HasValue)
        {
            var delta = oldest.NvmeAvailableSpare.Value - newest.NvmeAvailableSpare.Value;
            if (delta > 0)
            {
                var severity = newest.NvmeAvailableSpare.Value <= 10 ? "Critical" :
                    newest.NvmeAvailableSpare.Value <= 25 ? "Warning" : "Info";
                trends.Add(new SmartTrend
                {
                    Attribute = "NVMe Available Spare",
                    Direction = "Decreasing",
                    Severity = severity,
                    Message = $"Available spare dropped {delta}% over last {recent.Count} readings (now {newest.NvmeAvailableSpare}%)"
                });
            }
        }

        if (oldest.Temperature.HasValue && newest.Temperature.HasValue)
        {
            var avg = recent.Where(r => r.Temperature.HasValue).Average(r => r.Temperature!.Value);
            if (avg >= 55)
            {
                trends.Add(new SmartTrend
                {
                    Attribute = "Temperature",
                    Direction = avg >= 60 ? "Increasing" : "Stable",
                    Severity = avg >= 65 ? "Critical" : "Warning",
                    Message = $"Average temperature {avg:F0} C over last {recent.Count} readings"
                });
            }
        }

        return trends;
    }

    private static void CheckMonotonicIncrease(List<SmartTrend> trends, List<SmartReading> recent,
        Func<SmartReading, long?> selector, string name, string message, string severity)
    {
        var oldest = selector(recent.First());
        var newest = selector(recent.Last());
        if (!oldest.HasValue || !newest.HasValue) return;
        if (newest.Value <= oldest.Value) return;

        trends.Add(new SmartTrend
        {
            Attribute = name,
            Direction = "Increasing",
            Severity = severity,
            Message = $"{name}: {oldest.Value} -> {newest.Value} — {message}"
        });
    }

    private async Task<List<SmartReading>> LoadReadingsAsync(string deviceId)
    {
        var path = GetFilePath(deviceId);
        if (!File.Exists(path)) return new List<SmartReading>();

        string json;
        try { json = await File.ReadAllTextAsync(path); }
        catch { return new List<SmartReading>(); }

        try
        {
            var envelope = JsonSerializer.Deserialize<SmartHistoryEnvelope>(json, JsonOpts);
            if (envelope?.SchemaVersion >= 1 && envelope.Readings is not null)
                return envelope.Readings;
        }
        catch { }

        try
        {
            var legacy = JsonSerializer.Deserialize<List<SmartReading>>(json, JsonOpts);
            if (legacy is not null) return legacy;
        }
        catch { }

        QuarantineCorruptFile(path);
        return new List<SmartReading>();
    }

    private static void QuarantineCorruptFile(string path)
    {
        try
        {
            var corruptPath = path + ".corrupt";
            if (File.Exists(corruptPath)) File.Delete(corruptPath);
            File.Move(path, corruptPath);
        }
        catch { }
    }

    public async Task<string> ExportAsync(string deviceId, string destinationPath)
    {
        var readings = await LoadReadingsAsync(deviceId);
        if (readings.Count == 0)
            throw new InvalidOperationException($"No SMART history for device {deviceId}.");

        var envelope = new SmartHistoryEnvelope
        {
            SchemaVersion = CurrentSchemaVersion,
            Readings = readings
        };

        var redacted = System.Text.RegularExpressions.Regex.Replace(
            JsonSerializer.Serialize(envelope, JsonOpts),
            @"(?i)[A-Z]:\\[^\s""]+", "[path]");

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
        await File.WriteAllTextAsync(destinationPath, redacted);
        return destinationPath;
    }

    public async Task<int> ImportAsync(string sourcePath, string deviceId)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Import file not found.", sourcePath);

        var json = await File.ReadAllTextAsync(sourcePath);

        List<SmartReading>? imported = null;

        try
        {
            var envelope = JsonSerializer.Deserialize<SmartHistoryEnvelope>(json, JsonOpts);
            if (envelope?.Readings is not null)
                imported = envelope.Readings;
        }
        catch { }

        if (imported is null)
        {
            try { imported = JsonSerializer.Deserialize<List<SmartReading>>(json, JsonOpts); }
            catch { }
        }

        if (imported is null || imported.Count == 0)
            throw new InvalidOperationException("Import file contains no valid SMART readings.");

        var existing = await LoadReadingsAsync(deviceId);
        var existingTimestamps = new HashSet<long>(existing.Select(r => r.Timestamp.ToUnixTimeSeconds()));
        var newReadings = imported.Where(r => !existingTimestamps.Contains(r.Timestamp.ToUnixTimeSeconds())).ToList();

        existing.AddRange(newReadings);
        existing.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        if (existing.Count > MaxReadingsPerDevice)
            existing.RemoveRange(0, existing.Count - MaxReadingsPerDevice);

        Directory.CreateDirectory(HistoryDir);
        var path = GetFilePath(deviceId);
        var saveEnvelope = new SmartHistoryEnvelope
        {
            SchemaVersion = CurrentSchemaVersion,
            Readings = existing
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(saveEnvelope, JsonOpts));

        return newReadings.Count;
    }

    public async Task SetRetentionAsync(string deviceId, int maxDays)
    {
        var readings = await LoadReadingsAsync(deviceId);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-maxDays);
        var filtered = readings.Where(r => r.Timestamp >= cutoff).ToList();

        if (filtered.Count == readings.Count) return;

        Directory.CreateDirectory(HistoryDir);
        var path = GetFilePath(deviceId);
        var envelope = new SmartHistoryEnvelope
        {
            SchemaVersion = CurrentSchemaVersion,
            Readings = filtered
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(envelope, JsonOpts));
    }

    public static string FormatHtmlReport(PhysicalDiskInfo disk, SmartData? smart,
        List<SmartReading> history, List<SmartTrend> trends, List<AlignmentInfo> alignments)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'>");
        sb.AppendLine("<title>SMART Health Report</title>");
        sb.AppendLine("<style>body{font-family:system-ui;background:#1e1e2e;color:#cdd6f4;margin:2em;}" +
            "table{border-collapse:collapse;margin:1em 0}th,td{border:1px solid #45475a;padding:6px 12px;text-align:left}" +
            "th{background:#313244}h1{color:#89b4fa}h2{color:#a6adc8;border-bottom:1px solid #45475a;padding-bottom:4px}" +
            ".good{color:#a6e3a1}.warn{color:#f9e2af}.crit{color:#f38ba8}</style>");
        sb.AppendLine("</head><body>");

        sb.AppendLine($"<h1>SMART Health Report</h1>");
        sb.AppendLine($"<p>Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}</p>");

        sb.AppendLine("<h2>Drive Information</h2><table>");
        sb.AppendLine($"<tr><td>Device ID</td><td>{disk.DeviceId}</td></tr>");
        sb.AppendLine($"<tr><td>Model</td><td>{disk.FriendlyName}</td></tr>");
        sb.AppendLine($"<tr><td>Size</td><td>{SizeUtil.Format(disk.Size)}</td></tr>");
        sb.AppendLine($"<tr><td>Media Type</td><td>{disk.MediaType}</td></tr>");
        sb.AppendLine($"<tr><td>Bus Type</td><td>{disk.BusType}</td></tr>");
        sb.AppendLine($"<tr><td>Firmware</td><td>{disk.FirmwareVersion}</td></tr>");
        sb.AppendLine("</table>");

        if (smart is not null)
        {
            var healthClass = smart.Health switch { HealthStatus.Good => "good", HealthStatus.Warning => "warn", _ => "crit" };
            sb.AppendLine("<h2>Health Status</h2>");
            sb.AppendLine($"<p class='{healthClass}'><strong>{smart.Health}</strong> &mdash; {smart.HealthReason}</p>");

            sb.AppendLine("<h2>SMART Attributes</h2><table><tr><th>Metric</th><th>Value</th></tr>");
            if (smart.Temperature.HasValue) sb.AppendLine($"<tr><td>Temperature</td><td>{smart.Temperature} C</td></tr>");
            if (smart.Wear.HasValue) sb.AppendLine($"<tr><td>Wear</td><td>{smart.Wear}%</td></tr>");
            if (smart.PowerOnHours.HasValue) sb.AppendLine($"<tr><td>Power-On Hours</td><td>{smart.PowerOnHours:N0}</td></tr>");
            if (smart.PowerCycleCount.HasValue) sb.AppendLine($"<tr><td>Power Cycles</td><td>{smart.PowerCycleCount:N0}</td></tr>");
            if (smart.ReallocatedSectors.HasValue) sb.AppendLine($"<tr><td>Reallocated Sectors</td><td>{smart.ReallocatedSectors:N0}</td></tr>");
            if (smart.PendingSectors.HasValue) sb.AppendLine($"<tr><td>Pending Sectors</td><td>{smart.PendingSectors:N0}</td></tr>");
            if (smart.TotalBytesWritten.HasValue) sb.AppendLine($"<tr><td>Total Written</td><td>{SizeUtil.Format(smart.TotalBytesWritten.Value)}</td></tr>");
            if (smart.TotalBytesRead.HasValue) sb.AppendLine($"<tr><td>Total Read</td><td>{SizeUtil.Format(smart.TotalBytesRead.Value)}</td></tr>");
            if (smart.NvmeAvailableSpare.HasValue) sb.AppendLine($"<tr><td>NVMe Available Spare</td><td>{smart.NvmeAvailableSpare}%</td></tr>");
            if (smart.NvmeMediaErrors.HasValue) sb.AppendLine($"<tr><td>NVMe Media Errors</td><td>{smart.NvmeMediaErrors:N0}</td></tr>");
            if (smart.NvmeUnsafeShutdowns.HasValue) sb.AppendLine($"<tr><td>Unsafe Shutdowns</td><td>{smart.NvmeUnsafeShutdowns:N0}</td></tr>");
            if (smart.NvmeControllerBusyMinutes.HasValue) sb.AppendLine($"<tr><td>Controller Busy Time</td><td>{smart.NvmeControllerBusyMinutes:N0} min</td></tr>");
            if (smart.NvmeErrorLogEntries.HasValue) sb.AppendLine($"<tr><td>Error Log Entries</td><td>{smart.NvmeErrorLogEntries:N0}</td></tr>");
            if (smart.CriticalWarningFlags.Count > 0) sb.AppendLine($"<tr><td>Critical Warnings</td><td class='crit'>{string.Join(", ", smart.CriticalWarningFlags)}</td></tr>");
            sb.AppendLine("</table>");

            if (smart.AllAttributes.Count > 0)
            {
                sb.AppendLine("<h2>Raw SMART Attributes</h2><table><tr><th>ID</th><th>Name</th><th>Current</th><th>Worst</th><th>Raw</th></tr>");
                foreach (var a in smart.AllAttributes)
                    sb.AppendLine($"<tr><td>{a.Id}</td><td>{a.Name}</td><td>{a.Current}</td><td>{a.Worst}</td><td>{a.RawDisplay}</td></tr>");
                sb.AppendLine("</table>");
            }
        }

        if (trends.Count > 0)
        {
            sb.AppendLine("<h2>Trend Analysis</h2><table><tr><th>Severity</th><th>Attribute</th><th>Direction</th><th>Message</th></tr>");
            foreach (var t in trends)
            {
                var cls = t.Severity == "Critical" ? "crit" : t.Severity == "Warning" ? "warn" : "";
                sb.AppendLine($"<tr class='{cls}'><td>{t.Severity}</td><td>{t.Attribute}</td><td>{t.Direction}</td><td>{t.Message}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        if (history.Count > 0)
        {
            sb.AppendLine($"<h2>History ({history.Count} readings)</h2><table>");
            sb.AppendLine("<tr><th>Timestamp</th><th>Temp</th><th>Wear</th><th>Realloc</th><th>Written</th></tr>");
            foreach (var r in history.TakeLast(20))
            {
                sb.AppendLine($"<tr><td>{r.Timestamp:yyyy-MM-dd HH:mm}</td><td>{r.Temperature?.ToString() ?? "-"}</td>" +
                    $"<td>{(r.Wear.HasValue ? $"{r.Wear}%" : "-")}</td><td>{r.ReallocatedSectors?.ToString("N0") ?? "-"}</td>" +
                    $"<td>{(r.TotalBytesWritten.HasValue ? SizeUtil.Format(r.TotalBytesWritten.Value) : "-")}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        var diskAlignments = alignments.Where(a => a.DiskNumber.ToString() == disk.DeviceId).ToList();
        if (diskAlignments.Count > 0)
        {
            sb.AppendLine("<h2>4K Alignment</h2><table><tr><th>Part</th><th>Letter</th><th>Offset</th><th>Aligned</th></tr>");
            foreach (var a in diskAlignments)
                sb.AppendLine($"<tr><td>{a.PartitionNumber}</td><td>{a.LetterDisplay}</td><td>{a.Offset}</td><td>{a.AlignedDisplay}</td></tr>");
            sb.AppendLine("</table>");
        }

        sb.AppendLine("<p style='color:#585b70;margin-top:2em;font-size:12px'>Generated by PartitionPilot</p>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string GetFilePath(string deviceId) =>
        Path.Combine(HistoryDir, $"device_{SanitizeDeviceId(deviceId)}.json");

    private static string SanitizeDeviceId(string id) =>
        new string(id.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
}
