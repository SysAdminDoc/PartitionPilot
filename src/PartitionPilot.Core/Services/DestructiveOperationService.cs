namespace PartitionPilot;

public sealed record DestructiveOperationRequest(
    int DiskNumber,
    DiskIdentitySnapshot Identity,
    string OperationName,
    bool LockVolumes = true);

public sealed record DestructiveOperationOutcome(string SnapshotPath, IReadOnlyList<char> LockedVolumes);

/// <summary>
/// Runs the gate sequence every destructive disk operation has to pass, in one place.
/// <para>
/// Re-verify the target's identity, save the mandatory pre-destruction partition snapshot, lock and
/// dismount the volumes involved, then execute. The order matters: the snapshot has to exist before
/// anything is written, and the identity check has to happen after the operator confirmed but before
/// the first byte moves, so a disk that was swapped or re-enumerated in between cannot be hit.
/// </para>
/// <para>
/// This lives in Core so the CLI enforces exactly what the GUI enforces. A rescue tool that skips the
/// snapshot because it took a different code path would be worse than no rescue tool.
/// </para>
/// </summary>
public static class DestructiveOperationService
{
    public static async Task<DestructiveOperationOutcome> RunAsync(
        DestructiveOperationRequest request,
        IWmiDiskService wmiService,
        PartitionTableBackup backup,
        IActivityLog log,
        Func<CancellationToken, Task> execute,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(wmiService);
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(execute);

        // Fail closed before anything is written: the disk may have been swapped or re-enumerated
        // between the operator confirming and this running.
        await request.Identity.VerifyCurrentAsync(wmiService);

        log.Log($"Saving required pre-destruction snapshot before {request.OperationName} on Disk {request.DiskNumber}...");
        var snapshotPath = await backup.SaveSnapshotForDestructiveOperationAsync(
            request.DiskNumber, request.OperationName, ct);

        var locks = new List<VolumeLock>();
        var lockedLetters = new List<char>();
        try
        {
            if (request.LockVolumes)
            {
                foreach (var partition in await wmiService.GetPartitionsAsync(request.DiskNumber))
                {
                    if (partition.DriveLetter is not { } letter)
                        continue;

                    locks.Add(VolumeLockService.RequireLock(letter, log));
                    lockedLetters.Add(letter);
                }
            }

            await execute(ct);
        }
        finally
        {
            foreach (var volumeLock in locks)
            {
                try { volumeLock.Dispose(); }
                catch (Exception ex) { log.Log($"Releasing a volume lock failed: {ex.Message}"); }
            }
        }

        return new DestructiveOperationOutcome(snapshotPath, lockedLetters);
    }
}
