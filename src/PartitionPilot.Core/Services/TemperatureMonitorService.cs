namespace PartitionPilot;

public class TemperatureAlert
{
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public int Temperature { get; set; }
    public string Severity { get; set; } = "Info";
    public string Message { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
}

public class TemperatureMonitorService
{
    public const int WarningThreshold = 55;
    public const int CriticalThreshold = 65;

    private readonly IWmiDiskService _wmi;
    private readonly IActivityLog _log;
    private CancellationTokenSource? _cts;
    private readonly List<TemperatureAlert> _alerts = new();
    private readonly Dictionary<string, int> _lastTemperatures = new();
    private readonly object _lock = new();

    public event EventHandler<TemperatureAlert>? AlertRaised;
    public event EventHandler<Dictionary<string, int>>? TemperaturesUpdated;

    public TemperatureMonitorService(IWmiDiskService wmi, IActivityLog log)
    {
        _wmi = wmi;
        _log = log;
    }

    public bool IsRunning => _cts is not null && !_cts.IsCancellationRequested;

    public IReadOnlyDictionary<string, int> LastTemperatures
    {
        get { lock (_lock) return new Dictionary<string, int>(_lastTemperatures); }
    }

    public IReadOnlyList<TemperatureAlert> RecentAlerts
    {
        get { lock (_lock) return _alerts.ToList(); }
    }

    public void Start(TimeSpan interval)
    {
        Stop();
        _cts = new CancellationTokenSource();
        _ = PollLoopAsync(interval, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public async Task PollOnceAsync()
    {
        try
        {
            var physicals = await _wmi.GetPhysicalDisksAsync();
            var updated = new Dictionary<string, int>();

            foreach (var disk in physicals)
            {
                var smart = await _wmi.GetSmartDataAsync(disk.DeviceId);
                if (smart?.Temperature is not int temp) continue;

                updated[disk.DeviceId] = temp;

                lock (_lock)
                    _lastTemperatures[disk.DeviceId] = temp;

                if (temp >= CriticalThreshold)
                    RaiseAlert(disk.DeviceId, disk.FriendlyName, temp, "Critical",
                        $"{disk.FriendlyName} temperature critically high at {temp} C — risk of thermal shutdown");
                else if (temp >= WarningThreshold)
                    RaiseAlert(disk.DeviceId, disk.FriendlyName, temp, "Warning",
                        $"{disk.FriendlyName} temperature elevated at {temp} C — check airflow");
            }

            TemperaturesUpdated?.Invoke(this, updated);
        }
        catch (Exception ex)
        {
            _log.Log($"Temperature monitor poll failed: {ex.Message}");
        }
    }

    private async Task PollLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        _log.Log("Temperature monitoring started.");
        while (!ct.IsCancellationRequested)
        {
            await PollOnceAsync();
            try { await Task.Delay(interval, ct); } catch (TaskCanceledException) { break; }
        }
        _log.Log("Temperature monitoring stopped.");
    }

    private void RaiseAlert(string deviceId, string name, int temp, string severity, string message)
    {
        var alert = new TemperatureAlert
        {
            DeviceId = deviceId,
            DeviceName = name,
            Temperature = temp,
            Severity = severity,
            Message = message,
            Timestamp = DateTimeOffset.Now
        };

        lock (_lock)
        {
            _alerts.Add(alert);
            if (_alerts.Count > 100) _alerts.RemoveAt(0);
        }

        _log.Log($"Temperature alert ({severity}): {message}");
        AlertRaised?.Invoke(this, alert);
    }
}
