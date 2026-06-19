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

    private static string GetFilePath(string deviceId) =>
        Path.Combine(HistoryDir, $"device_{SanitizeDeviceId(deviceId)}.json");

    private static string SanitizeDeviceId(string id) =>
        new string(id.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
}
