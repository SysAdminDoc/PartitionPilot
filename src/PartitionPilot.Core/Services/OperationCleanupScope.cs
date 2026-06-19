using System.IO;

namespace PartitionPilot;

public sealed class OperationCleanupScope : IDisposable, IAsyncDisposable
{
    private readonly IActivityLog _log;
    private readonly List<CleanupRegistration> _registrations = new();
    private bool _disposed;

    public OperationCleanupScope(IActivityLog log)
    {
        _log = log;
    }

    public CleanupRegistration Register(string description, Func<Task> cleanup, string recoveryHint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var registration = new CleanupRegistration(description, cleanup, recoveryHint);
        _registrations.Add(registration);
        return registration;
    }

    public CleanupRegistration RegisterFileDelete(string path)
    {
        return Register(
            $"Delete temporary file {path}",
            () =>
            {
                if (File.Exists(path))
                    File.Delete(path);
                return Task.CompletedTask;
            },
            $"Delete the temporary file manually if it remains: {path}");
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        for (var i = _registrations.Count - 1; i >= 0; i--)
        {
            var registration = _registrations[i];
            if (!registration.IsActive)
                continue;

            try
            {
                _log.Log($"Cleanup: {registration.Description}...");
                await registration.Cleanup();
                registration.Complete();
                _log.Log($"Cleanup complete: {registration.Description}.");
            }
            catch (Exception ex)
            {
                _log.Log($"Cleanup failed: {registration.Description}: {ex.Message}. Recovery: {registration.RecoveryHint}");
            }
        }
    }

    public sealed class CleanupRegistration
    {
        internal CleanupRegistration(string description, Func<Task> cleanup, string recoveryHint)
        {
            Description = description;
            Cleanup = cleanup;
            RecoveryHint = recoveryHint;
        }

        public string Description { get; }
        public Func<Task> Cleanup { get; }
        public string RecoveryHint { get; }
        public bool IsActive { get; private set; } = true;

        public void Complete()
        {
            IsActive = false;
        }
    }
}
