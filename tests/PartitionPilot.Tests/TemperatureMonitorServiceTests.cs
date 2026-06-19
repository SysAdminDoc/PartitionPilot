namespace PartitionPilot.Tests;

public class TemperatureMonitorServiceTests
{
    private sealed class FakeLog : IActivityLog
    {
        public List<string> Messages { get; } = new();
        public void Log(string message) => Messages.Add(message);
    }

    private sealed class FakeWmiService : IWmiDiskService
    {
        public List<PhysicalDiskInfo> Disks { get; set; } = new();
        public Dictionary<string, SmartData> SmartByDeviceId { get; set; } = new();

        public Task<List<PhysicalDiskInfo>> GetPhysicalDisksAsync() => Task.FromResult(Disks);
        public Task<SmartData?> GetSmartDataAsync(string deviceId) =>
            Task.FromResult(SmartByDeviceId.TryGetValue(deviceId, out var s) ? s : null);

        public Task<List<DiskInfo>> GetDisksAsync() => Task.FromResult(new List<DiskInfo>());
        public Task<List<PartitionInfo>> GetPartitionsAsync(int diskNumber) => Task.FromResult(new List<PartitionInfo>());
        public Task<List<VolumeInfo>> GetVolumesAsync() => Task.FromResult(new List<VolumeInfo>());
        public Task<List<AlignmentInfo>> GetAlignmentAuditAsync() => Task.FromResult(new List<AlignmentInfo>());
        public Task<HashSet<char>> GetPagefileLocationsAsync() => Task.FromResult(new HashSet<char>());
        public Task<List<char>> GetAvailableLettersAsync() => Task.FromResult(new List<char>());
        public Task<(long Min, long Max)> GetPartitionSupportedSizeAsync(char driveLetter) => Task.FromResult((0L, 0L));
        public Task<List<MountedImageInfo>> GetMountedImagesAsync() => Task.FromResult(new List<MountedImageInfo>());
        public Task<Dictionary<char, string>> GetBitLockerStatusAsync() => Task.FromResult(new Dictionary<char, string>());
        public Task<List<string>> GetBitLockerProtectedTargetsAsync(int diskNumber) => Task.FromResult(new List<string>());
        public Task<Dictionary<int, string>> GetStoragePoolMembershipAsync() => Task.FromResult(new Dictionary<int, string>());
    }

    [Fact]
    public async Task PollOnce_RaisesWarningAt55C()
    {
        var log = new FakeLog();
        var wmi = new FakeWmiService
        {
            Disks = [new() { DeviceId = "0", FriendlyName = "Test SSD" }],
            SmartByDeviceId = { ["0"] = new SmartData { Temperature = 57 } }
        };
        var monitor = new TemperatureMonitorService(wmi, log);
        TemperatureAlert? raised = null;
        monitor.AlertRaised += (_, a) => raised = a;

        await monitor.PollOnceAsync();

        Assert.NotNull(raised);
        Assert.Equal("Warning", raised.Severity);
        Assert.Equal(57, raised.Temperature);
    }

    [Fact]
    public async Task PollOnce_RaisesCriticalAt65C()
    {
        var log = new FakeLog();
        var wmi = new FakeWmiService
        {
            Disks = [new() { DeviceId = "0", FriendlyName = "Test SSD" }],
            SmartByDeviceId = { ["0"] = new SmartData { Temperature = 70 } }
        };
        var monitor = new TemperatureMonitorService(wmi, log);
        TemperatureAlert? raised = null;
        monitor.AlertRaised += (_, a) => raised = a;

        await monitor.PollOnceAsync();

        Assert.NotNull(raised);
        Assert.Equal("Critical", raised.Severity);
        Assert.Equal(70, raised.Temperature);
    }

    [Fact]
    public async Task PollOnce_NoAlertBelowWarningThreshold()
    {
        var log = new FakeLog();
        var wmi = new FakeWmiService
        {
            Disks = [new() { DeviceId = "0", FriendlyName = "Test SSD" }],
            SmartByDeviceId = { ["0"] = new SmartData { Temperature = 40 } }
        };
        var monitor = new TemperatureMonitorService(wmi, log);
        TemperatureAlert? raised = null;
        monitor.AlertRaised += (_, a) => raised = a;

        await monitor.PollOnceAsync();

        Assert.Null(raised);
    }

    [Fact]
    public async Task PollOnce_UpdatesLastTemperatures()
    {
        var log = new FakeLog();
        var wmi = new FakeWmiService
        {
            Disks = [new() { DeviceId = "1", FriendlyName = "Test HDD" }],
            SmartByDeviceId = { ["1"] = new SmartData { Temperature = 42 } }
        };
        var monitor = new TemperatureMonitorService(wmi, log);

        await monitor.PollOnceAsync();

        Assert.Equal(42, monitor.LastTemperatures["1"]);
    }

    [Fact]
    public async Task PollOnce_FiresTemperaturesUpdatedEvent()
    {
        var log = new FakeLog();
        var wmi = new FakeWmiService
        {
            Disks = [new() { DeviceId = "0", FriendlyName = "Test" }],
            SmartByDeviceId = { ["0"] = new SmartData { Temperature = 35 } }
        };
        var monitor = new TemperatureMonitorService(wmi, log);
        Dictionary<string, int>? updated = null;
        monitor.TemperaturesUpdated += (_, d) => updated = d;

        await monitor.PollOnceAsync();

        Assert.NotNull(updated);
        Assert.Equal(35, updated["0"]);
    }

    [Fact]
    public async Task PollOnce_SkipsDisksWithoutTemperature()
    {
        var log = new FakeLog();
        var wmi = new FakeWmiService
        {
            Disks = [new() { DeviceId = "0", FriendlyName = "USB" }],
            SmartByDeviceId = { ["0"] = new SmartData { Temperature = null } }
        };
        var monitor = new TemperatureMonitorService(wmi, log);
        TemperatureAlert? raised = null;
        monitor.AlertRaised += (_, a) => raised = a;

        await monitor.PollOnceAsync();

        Assert.Null(raised);
        Assert.Empty(monitor.LastTemperatures);
    }

    [Fact]
    public async Task PollOnce_HandlesWmiFailureGracefully()
    {
        var log = new FakeLog();
        var wmi = new FakeWmiService();
        var monitor = new TemperatureMonitorService(wmi, log);

        await monitor.PollOnceAsync();

        Assert.Empty(monitor.LastTemperatures);
    }

    [Fact]
    public async Task RecentAlerts_CapsAt100()
    {
        var log = new FakeLog();
        var wmi = new FakeWmiService
        {
            Disks = [new() { DeviceId = "0", FriendlyName = "Hot" }],
            SmartByDeviceId = { ["0"] = new SmartData { Temperature = 70 } }
        };
        var monitor = new TemperatureMonitorService(wmi, log);

        for (int i = 0; i < 105; i++)
            await monitor.PollOnceAsync();

        Assert.Equal(100, monitor.RecentAlerts.Count);
    }

    [Fact]
    public void StartStop_SetsIsRunning()
    {
        var log = new FakeLog();
        var wmi = new FakeWmiService();
        var monitor = new TemperatureMonitorService(wmi, log);

        Assert.False(monitor.IsRunning);

        monitor.Start(TimeSpan.FromHours(1));
        Assert.True(monitor.IsRunning);

        monitor.Stop();
        Assert.False(monitor.IsRunning);
    }

    [Fact]
    public void ThresholdConstants_AreReasonable()
    {
        Assert.Equal(55, TemperatureMonitorService.WarningThreshold);
        Assert.Equal(65, TemperatureMonitorService.CriticalThreshold);
        Assert.True(TemperatureMonitorService.WarningThreshold < TemperatureMonitorService.CriticalThreshold);
    }
}
