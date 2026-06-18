namespace PartitionPilot;

public static class BitLockerPreflight
{
    public static string MapStatus(int protectionStatus, int? lockStatus)
    {
        var status = protectionStatus switch
        {
            0 => "BitLocker: Off",
            1 => "BitLocker: On",
            2 => "BitLocker: Unknown",
            _ => "BitLocker: Unknown"
        };

        if (status == "BitLocker: Off" || lockStatus is null)
            return status;

        var lockText = lockStatus switch
        {
            0 => "Unlocked",
            1 => "Locked",
            _ => "Lock status unknown"
        };

        return $"{status} ({lockText})";
    }

    public static bool IsProtected(string? encryptionStatus)
    {
        if (string.IsNullOrWhiteSpace(encryptionStatus))
            return false;

        return encryptionStatus.Contains("BitLocker: On", StringComparison.OrdinalIgnoreCase) ||
               encryptionStatus.Contains("BitLocker: Unknown", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLocked(string? encryptionStatus)
    {
        return !string.IsNullOrWhiteSpace(encryptionStatus) &&
               encryptionStatus.Contains("Locked", StringComparison.OrdinalIgnoreCase) &&
               !encryptionStatus.Contains("Unlocked", StringComparison.OrdinalIgnoreCase);
    }

    public static bool RequiresSuspensionForMutation(string? encryptionStatus) => IsProtected(encryptionStatus);

    public static bool RequiresUnlockForRead(string? encryptionStatus)
    {
        return IsLocked(encryptionStatus) ||
               (!string.IsNullOrWhiteSpace(encryptionStatus) &&
                encryptionStatus.Contains("Unknown", StringComparison.OrdinalIgnoreCase));
    }

    public static string Describe(string? encryptionStatus)
    {
        return string.IsNullOrWhiteSpace(encryptionStatus)
            ? "BitLocker: Not reported"
            : encryptionStatus;
    }

    public static string BuildMutationBlockedMessage(string operation, string target, string? encryptionStatus)
    {
        return $"{operation} is blocked for {target} because BitLocker protection is active or unknown.\n\n" +
               $"Encryption state: {Describe(encryptionStatus)}\n\n" +
               "Suspend BitLocker protection, unlock the volume if needed, refresh PartitionPilot, then retry.";
    }

    public static string BuildUnlockRequiredMessage(string operation, string target, string? encryptionStatus)
    {
        return $"{operation} requires {target} to be unlocked first.\n\n" +
               $"Encryption state: {Describe(encryptionStatus)}\n\n" +
               "Unlock the volume in Windows, refresh PartitionPilot, then retry.";
    }

    public static string BuildDestructiveConfirmation(string operation, IEnumerable<string> protectedTargets)
    {
        var targets = protectedTargets.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var targetText = targets.Count == 0 ? "BitLocker-protected data" : string.Join("\n", targets.Select(t => $"  - {t}"));

        return $"{operation} will target BitLocker-protected data:\n{targetText}\n\n" +
               "This can permanently destroy encrypted contents, recovery metadata, and any data protected by recovery keys. " +
               "Continue only if backups and recovery keys are available.";
    }

    public static string DescribePartitionTarget(PartitionInfo partition)
    {
        var label = partition.DriveLetter.HasValue
            ? $"{partition.DriveLetter}:"
            : $"Partition {partition.PartitionNumber}";

        return $"{label} {Describe(partition.EncryptionStatus)}";
    }
}
