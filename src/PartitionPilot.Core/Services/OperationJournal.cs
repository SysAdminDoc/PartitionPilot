using System.IO;
using System.Text.Json;

namespace PartitionPilot;

public enum JournalEntryStatus
{
    Queued,
    Applying,
    Completed,
    Failed,
    Skipped,
    Discarded
}

public class JournalEntry
{
    public int Index { get; set; }
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public string DiskTarget { get; set; } = "";
    public string RiskLevel { get; set; } = "Normal";
    public JournalEntryStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public class OperationJournal
{
    public string Id { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string State { get; set; } = "active";
    public List<JournalEntry> Entries { get; set; } = new();
}

public static class OperationJournalService
{
    private static readonly string JournalDir = ResolveJournalDir();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string JournalDirectory => JournalDir;

    private static string ResolveJournalDir()
    {
        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "portable.txt")))
            return Path.Combine(exeDir, "journals");
        var programData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PartitionPilot", "journals");
        try
        {
            Directory.CreateDirectory(programData);
            return programData;
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), "PartitionPilot", "journals");
        }
    }

    public static OperationJournal CreateJournal(IReadOnlyList<PendingOperation> operations)
    {
        var journal = new OperationJournal
        {
            Id = $"journal_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}".Substring(0, 48),
            CreatedAt = DateTimeOffset.UtcNow,
            State = "active",
            Entries = operations.Select((op, i) => new JournalEntry
            {
                Index = i,
                Type = op.Type.ToString(),
                Description = RedactPaths(op.Description),
                DiskTarget = op.DiskTarget,
                RiskLevel = op.RiskLevel,
                Status = JournalEntryStatus.Queued,
                Timestamp = DateTimeOffset.UtcNow
            }).ToList()
        };
        return journal;
    }

    public static async Task SaveAsync(OperationJournal journal)
    {
        Directory.CreateDirectory(JournalDir);
        var path = GetJournalPath(journal.Id);
        var json = JsonSerializer.Serialize(journal, JsonOpts);
        await File.WriteAllTextAsync(path, json);
    }

    public static void UpdateEntry(OperationJournal journal, int index, JournalEntryStatus status, string? errorMessage = null)
    {
        if (index >= 0 && index < journal.Entries.Count)
        {
            journal.Entries[index].Status = status;
            journal.Entries[index].ErrorMessage = errorMessage;
            journal.Entries[index].Timestamp = DateTimeOffset.UtcNow;
        }
    }

    public static void MarkCompleted(OperationJournal journal)
    {
        journal.State = "completed";
        journal.CompletedAt = DateTimeOffset.UtcNow;
    }

    public static void MarkInterrupted(OperationJournal journal)
    {
        journal.State = "interrupted";
        foreach (var entry in journal.Entries.Where(e => e.Status == JournalEntryStatus.Queued))
            entry.Status = JournalEntryStatus.Skipped;
    }

    public static async Task<List<OperationJournal>> LoadInterruptedJournalsAsync()
    {
        var interrupted = new List<OperationJournal>();
        if (!Directory.Exists(JournalDir)) return interrupted;

        foreach (var file in Directory.EnumerateFiles(JournalDir, "journal_*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var journal = JsonSerializer.Deserialize<OperationJournal>(json, JsonOpts);
                if (journal?.State == "active")
                {
                    MarkInterrupted(journal);
                    interrupted.Add(journal);
                }
            }
            catch { }
        }

        return interrupted;
    }

    public static async Task DiscardJournalAsync(string journalId)
    {
        var path = GetJournalPath(journalId);
        if (!File.Exists(path)) return;

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var journal = JsonSerializer.Deserialize<OperationJournal>(json, JsonOpts);
            if (journal is not null)
            {
                journal.State = "discarded";
                journal.CompletedAt = DateTimeOffset.UtcNow;
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(journal, JsonOpts));
            }
        }
        catch
        {
            try { File.Delete(path); } catch { }
        }
    }

    public static void PurgeOldJournals(int maxAgeDays = 30)
    {
        if (!Directory.Exists(JournalDir)) return;
        var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);
        foreach (var file in Directory.EnumerateFiles(JournalDir, "journal_*.json"))
        {
            try
            {
                if (File.GetCreationTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
            catch { }
        }
    }

    private static string GetJournalPath(string journalId) =>
        Path.Combine(JournalDir, $"{journalId}.json");

    private static string RedactPaths(string text)
    {
        try
        {
            return System.Text.RegularExpressions.Regex.Replace(
                text, @"(?i)[A-Z]:\\[^\s""']+", "[path]");
        }
        catch { return text; }
    }
}
