namespace PartitionPilot.Tests;

public class OperationJournalTests
{
    [Fact]
    public void CreateJournal_SetsIdAndTimestamp()
    {
        var ops = new List<PendingOperation>
        {
            new() { Type = PendingOperationType.Format, Description = "Format C:", DiskTarget = "Disk 0", RiskLevel = "High" }
        };

        var journal = OperationJournalService.CreateJournal(ops);

        Assert.StartsWith("journal_", journal.Id);
        Assert.Equal("active", journal.State);
        Assert.Null(journal.CompletedAt);
        Assert.True(journal.CreatedAt > DateTimeOffset.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public void CreateJournal_MapsOperationsToEntries()
    {
        var ops = new List<PendingOperation>
        {
            new() { Type = PendingOperationType.Delete, Description = "Delete partition 2", DiskTarget = "Disk 0", RiskLevel = "High" },
            new() { Type = PendingOperationType.Create, Description = "Create 50 GB NTFS", DiskTarget = "Disk 0", RiskLevel = "Normal" }
        };

        var journal = OperationJournalService.CreateJournal(ops);

        Assert.Equal(2, journal.Entries.Count);
        Assert.Equal("Delete", journal.Entries[0].Type);
        Assert.Equal(JournalEntryStatus.Queued, journal.Entries[0].Status);
        Assert.Equal(0, journal.Entries[0].Index);
        Assert.Equal("Create", journal.Entries[1].Type);
        Assert.Equal(1, journal.Entries[1].Index);
    }

    [Fact]
    public void CreateJournal_RedactsPathsInDescription()
    {
        var ops = new List<PendingOperation>
        {
            new() { Type = PendingOperationType.Format, Description = @"Format C:\Users\Admin\data" }
        };

        var journal = OperationJournalService.CreateJournal(ops);

        Assert.DoesNotContain(@"C:\Users", journal.Entries[0].Description);
        Assert.Contains("[path]", journal.Entries[0].Description);
    }

    [Fact]
    public void UpdateEntry_ChangesStatusAndTimestamp()
    {
        var ops = new List<PendingOperation>
        {
            new() { Type = PendingOperationType.Format, Description = "Format" }
        };
        var journal = OperationJournalService.CreateJournal(ops);
        var before = journal.Entries[0].Timestamp;

        OperationJournalService.UpdateEntry(journal, 0, JournalEntryStatus.Completed);

        Assert.Equal(JournalEntryStatus.Completed, journal.Entries[0].Status);
        Assert.True(journal.Entries[0].Timestamp >= before);
    }

    [Fact]
    public void UpdateEntry_SetsErrorMessage()
    {
        var ops = new List<PendingOperation>
        {
            new() { Type = PendingOperationType.Delete, Description = "Delete" }
        };
        var journal = OperationJournalService.CreateJournal(ops);

        OperationJournalService.UpdateEntry(journal, 0, JournalEntryStatus.Failed, "Access denied");

        Assert.Equal(JournalEntryStatus.Failed, journal.Entries[0].Status);
        Assert.Equal("Access denied", journal.Entries[0].ErrorMessage);
    }

    [Fact]
    public void UpdateEntry_IgnoresOutOfRangeIndex()
    {
        var journal = OperationJournalService.CreateJournal(new List<PendingOperation>());

        OperationJournalService.UpdateEntry(journal, 5, JournalEntryStatus.Completed);
    }

    [Fact]
    public void MarkCompleted_SetsStateAndTimestamp()
    {
        var journal = OperationJournalService.CreateJournal(new List<PendingOperation>());

        OperationJournalService.MarkCompleted(journal);

        Assert.Equal("completed", journal.State);
        Assert.NotNull(journal.CompletedAt);
    }

    [Fact]
    public void MarkInterrupted_SkipsQueuedEntries()
    {
        var ops = new List<PendingOperation>
        {
            new() { Type = PendingOperationType.Delete, Description = "Op 1" },
            new() { Type = PendingOperationType.Create, Description = "Op 2" },
            new() { Type = PendingOperationType.Format, Description = "Op 3" }
        };
        var journal = OperationJournalService.CreateJournal(ops);
        OperationJournalService.UpdateEntry(journal, 0, JournalEntryStatus.Completed);

        OperationJournalService.MarkInterrupted(journal);

        Assert.Equal("interrupted", journal.State);
        Assert.Equal(JournalEntryStatus.Completed, journal.Entries[0].Status);
        Assert.Equal(JournalEntryStatus.Skipped, journal.Entries[1].Status);
        Assert.Equal(JournalEntryStatus.Skipped, journal.Entries[2].Status);
    }

    [Fact]
    public void CreateJournal_PreservesRiskLevel()
    {
        var ops = new List<PendingOperation>
        {
            new() { Type = PendingOperationType.Delete, Description = "Delete", RiskLevel = "Critical" }
        };

        var journal = OperationJournalService.CreateJournal(ops);

        Assert.Equal("Critical", journal.Entries[0].RiskLevel);
    }

    [Fact]
    public void CreateJournal_PreservesDiskTarget()
    {
        var ops = new List<PendingOperation>
        {
            new() { Type = PendingOperationType.Format, Description = "Format", DiskTarget = "Disk 1, Partition 3" }
        };

        var journal = OperationJournalService.CreateJournal(ops);

        Assert.Equal("Disk 1, Partition 3", journal.Entries[0].DiskTarget);
    }

    [Fact]
    public void CreateJournal_PreservesDiskIdentity()
    {
        var identity = new DiskIdentitySnapshot
        {
            DiskNumber = 1,
            FriendlyName = "Target Disk",
            Size = 1000,
            PartitionStyle = "GPT",
            UniqueId = "UID-1",
            SerialNumber = "SER-1",
            Path = @"\\?\disk#1"
        };
        var ops = new List<PendingOperation>
        {
            new() { Type = PendingOperationType.Delete, Description = "Delete", DiskTarget = identity.Summary, DiskIdentity = identity }
        };

        var journal = OperationJournalService.CreateJournal(ops);

        Assert.NotNull(journal.Entries[0].DiskIdentity);
        Assert.Equal("UID-1", journal.Entries[0].DiskIdentity!.UniqueId);
        Assert.Equal(@"\\?\disk#1", journal.Entries[0].DiskIdentity!.Path);
    }
}
