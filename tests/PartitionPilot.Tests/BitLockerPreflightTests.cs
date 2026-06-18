namespace PartitionPilot.Tests;

public class BitLockerPreflightTests
{
    [Theory]
    [InlineData(0, null, "BitLocker: Off")]
    [InlineData(1, 0, "BitLocker: On (Unlocked)")]
    [InlineData(1, 1, "BitLocker: On (Locked)")]
    [InlineData(2, null, "BitLocker: Unknown")]
    [InlineData(99, 5, "BitLocker: Unknown (Lock status unknown)")]
    public void MapStatus_IncludesProtectionAndLockState(int protectionStatus, int? lockStatus, string expected)
    {
        Assert.Equal(expected, BitLockerPreflight.MapStatus(protectionStatus, lockStatus));
    }

    [Theory]
    [InlineData("BitLocker: On (Locked)", true)]
    [InlineData("BitLocker: On (Unlocked)", false)]
    [InlineData("BitLocker: Unknown", false)]
    [InlineData("BitLocker: Off", false)]
    public void IsLocked_DoesNotTreatUnlockedAsLocked(string status, bool expected)
    {
        Assert.Equal(expected, BitLockerPreflight.IsLocked(status));
    }

    [Theory]
    [InlineData("BitLocker: On (Unlocked)", true)]
    [InlineData("BitLocker: Unknown", true)]
    [InlineData("BitLocker: Off", false)]
    [InlineData("", false)]
    public void RequiresSuspensionForMutation_BlocksProtectedOrUnknownVolumes(string status, bool expected)
    {
        Assert.Equal(expected, BitLockerPreflight.RequiresSuspensionForMutation(status));
    }

    [Theory]
    [InlineData("BitLocker: On (Locked)", true)]
    [InlineData("BitLocker: On (Unlocked)", false)]
    [InlineData("BitLocker: Unknown", true)]
    [InlineData("BitLocker: Off", false)]
    public void RequiresUnlockForRead_BlocksLockedOrUnknownVolumes(string status, bool expected)
    {
        Assert.Equal(expected, BitLockerPreflight.RequiresUnlockForRead(status));
    }

    [Fact]
    public void BuildDestructiveConfirmation_NamesProtectedTargets()
    {
        var message = BitLockerPreflight.BuildDestructiveConfirmation(
            "Wipe Disk 1",
            new[] { "C: BitLocker: On (Unlocked)" });

        Assert.Contains("Wipe Disk 1", message);
        Assert.Contains("C: BitLocker: On (Unlocked)", message);
        Assert.Contains("recovery keys", message);
    }
}
