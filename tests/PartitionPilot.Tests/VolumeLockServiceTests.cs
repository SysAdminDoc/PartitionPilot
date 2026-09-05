namespace PartitionPilot.Tests;

public class VolumeLockServiceTests
{
    /// <summary>A letter with no volume behind it, so the lock attempt fails without touching real storage.</summary>
    private const char UnusedDriveLetter = 'Q';

    [Fact]
    public void TryLock_ReturnsNullAndExplainsItselfWhenTheVolumeCannotBeOpened()
    {
        var log = new RecordingLog();

        var result = VolumeLockService.TryLock(UnusedDriveLetter, log);

        Assert.Null(result);
        Assert.Contains(log.Messages, m => m.Contains($"{UnusedDriveLetter}:", StringComparison.Ordinal));
    }

    [Fact]
    public void RequireLock_ThrowsRatherThanLettingADestructiveOperationRunUnlocked()
    {
        // The distinction is the point: TryLock is advisory, RequireLock is a gate. A wipe or clone that
        // proceeded on an unlocked volume would race whatever else is writing to it.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            VolumeLockService.RequireLock(UnusedDriveLetter, new RecordingLog()));

        Assert.Contains($"{UnusedDriveLetter}:", ex.Message);
        Assert.Contains("could not be locked", ex.Message);
    }

    [Fact]
    public void RequireLock_NormalisesTheDriveLetterInItsMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            VolumeLockService.RequireLock(char.ToLowerInvariant(UnusedDriveLetter)));

        Assert.Contains($"{UnusedDriveLetter}:", ex.Message);
    }

    [Fact]
    public void TryLock_ToleratesANullLog()
    {
        Assert.Null(VolumeLockService.TryLock(UnusedDriveLetter));
    }

    private sealed class RecordingLog : IActivityLog
    {
        public List<string> Messages { get; } = new();
        public void Log(string message) => Messages.Add(message);
    }
}
