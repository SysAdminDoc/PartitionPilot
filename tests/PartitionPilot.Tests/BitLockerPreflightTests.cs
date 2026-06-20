namespace PartitionPilot.Tests;

public class BitLockerPreflightTests
{
    [Theory]
    [InlineData(0, null, null, null, "BitLocker: Off")]
    [InlineData(1, 0, null, null, "BitLocker: On (Unlocked)")]
    [InlineData(1, 1, null, null, "BitLocker: On (Locked)")]
    [InlineData(2, null, null, null, "BitLocker: Unknown")]
    [InlineData(1, 0, 1, 6, "BitLocker: On (XTS-AES-128, Unlocked)")]
    [InlineData(1, 0, 1, 7, "BitLocker: On (XTS-AES-256, Unlocked)")]
    [InlineData(1, 1, 1, 4, "BitLocker: On (AES-256, Locked)")]
    [InlineData(0, null, 2, 6, "BitLocker: Encrypting (XTS-AES-128)")]
    [InlineData(0, null, 3, 7, "BitLocker: Decrypting (XTS-AES-256)")]
    [InlineData(1, 0, 4, 6, "BitLocker: Encryption Paused (XTS-AES-128, Unlocked)")]
    [InlineData(1, 0, 1, 0, "BitLocker: On (Unlocked)")]
    public void MapStatus_IncludesProtectionLockAndEncryption(
        int protectionStatus, int? lockStatus, int? conversionStatus, int? encryptionMethod, string expected)
    {
        Assert.Equal(expected, BitLockerPreflight.MapStatus(protectionStatus, lockStatus, conversionStatus, encryptionMethod));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(3, "AES-128")]
    [InlineData(4, "AES-256")]
    [InlineData(5, "Hardware")]
    [InlineData(6, "XTS-AES-128")]
    [InlineData(7, "XTS-AES-256")]
    public void MapEncryptionMethod_MapsKnownValues(int? method, string? expected)
    {
        Assert.Equal(expected, BitLockerPreflight.MapEncryptionMethod(method));
    }

    [Theory]
    [InlineData("BitLocker: On (Locked)", true)]
    [InlineData("BitLocker: On (Unlocked)", false)]
    [InlineData("BitLocker: On (XTS-AES-128, Locked)", true)]
    [InlineData("BitLocker: On (XTS-AES-256, Unlocked)", false)]
    [InlineData("BitLocker: Unknown", false)]
    [InlineData("BitLocker: Off", false)]
    public void IsLocked_DoesNotTreatUnlockedAsLocked(string status, bool expected)
    {
        Assert.Equal(expected, BitLockerPreflight.IsLocked(status));
    }

    [Theory]
    [InlineData("BitLocker: On (Unlocked)", true)]
    [InlineData("BitLocker: On (XTS-AES-128, Unlocked)", true)]
    [InlineData("BitLocker: Encrypting (XTS-AES-128)", true)]
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
