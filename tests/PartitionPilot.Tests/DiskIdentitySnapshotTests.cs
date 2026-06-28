namespace PartitionPilot.Tests;

public class DiskIdentitySnapshotTests
{
    [Fact]
    public void FromDisk_CapturesStableFieldsForDisplayAndPersistence()
    {
        var disk = TestDisk();

        var snapshot = DiskIdentitySnapshot.FromDisk(disk);

        Assert.Equal(7, snapshot.DiskNumber);
        Assert.Equal("UID-7", snapshot.UniqueId);
        Assert.Equal("SER-7", snapshot.SerialNumber);
        Assert.Equal(@"\\?\disk#7", snapshot.Path);
        Assert.Contains("UniqueId=UID-7", snapshot.StableIdentityText, StringComparison.Ordinal);
        Assert.Contains("Serial=SER-7", snapshot.StableIdentityText, StringComparison.Ordinal);
        Assert.Contains("Path=", snapshot.StableIdentityText, StringComparison.Ordinal);
    }

    [Fact]
    public void Matches_AllowsFriendlyNameChangesWhenStableIdentityAndSizeMatch()
    {
        var snapshot = DiskIdentitySnapshot.FromDisk(TestDisk());
        var current = TestDisk();
        current.FriendlyName = "Driver Renamed Disk";

        Assert.True(snapshot.Matches(current, out var mismatch));
        Assert.Equal("", mismatch);
    }

    [Fact]
    public void Matches_BlocksWhenUniqueIdChanges()
    {
        var snapshot = DiskIdentitySnapshot.FromDisk(TestDisk());
        var current = TestDisk();
        current.UniqueId = "OTHER";

        Assert.False(snapshot.Matches(current, out var mismatch));
        Assert.Contains("UniqueId changed", mismatch, StringComparison.Ordinal);
    }

    [Fact]
    public void Matches_BlocksWhenCurrentDiskNoLongerReportsStableIdentity()
    {
        var snapshot = DiskIdentitySnapshot.FromDisk(TestDisk());
        var current = TestDisk();
        current.UniqueId = "";
        current.SerialNumber = "";
        current.Path = "";

        Assert.False(snapshot.Matches(current, out var mismatch));
        Assert.Contains("stable identity", mismatch, StringComparison.OrdinalIgnoreCase);
    }

    private static DiskInfo TestDisk() => new()
    {
        Number = 7,
        FriendlyName = "Test Disk",
        Size = 512L * 1024 * 1024 * 1024,
        PartitionStyle = "GPT",
        UniqueId = "UID-7",
        SerialNumber = "SER-7",
        Path = @"\\?\disk#7",
        BusType = "NVMe",
        Location = "Bay 7"
    };
}
