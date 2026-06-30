namespace PartitionPilot.Tests;

public class SmartTestServiceTests
{
    [Fact]
    public async Task GetSmartctlInfoAsync_ReportsVersionAndPath()
    {
        var runner = new FakeRunner()
            .Respond("smartctl", "--version", "smartctl 7.5 2025-04-30 r5714 [x86_64-w64-mingw32] (sf-7.5-1)")
            .Respond("where.exe", "smartctl", "C:\\Tools\\smartctl.exe\r\n");

        var info = await SmartTestService.GetSmartctlInfoAsync(runner, new TestLog());

        Assert.True(info.IsAvailable);
        Assert.Equal("7.5", info.Version);
        Assert.Equal("C:\\Tools\\smartctl.exe", info.Path);
        Assert.Contains("path:", info.Detail);
    }

    [Fact]
    public async Task CheckSmartctlAsync_ReportsMissingToolWithRemediation()
    {
        var runner = new FakeRunner()
            .Throw("smartctl", "--version", new InvalidOperationException("not found"));

        var check = await EnvironmentDiagnostics.CheckSmartctlAsync(runner, new TestLog());

        Assert.Equal("Native Tools", check.Category);
        Assert.Equal("smartctl", check.Name);
        Assert.Equal("Error", check.Status);
        Assert.Contains("not found", check.Detail);
        Assert.Contains("Install smartmontools", check.Remediation);
    }

    [Fact]
    public void GetDeviceSpec_SelectsWindowsPhysicalDiskModes()
    {
        var sata = SmartTestService.GetDeviceSpec(new PhysicalDiskInfo { DeviceId = "0", BusType = "SATA" });
        var nvme = SmartTestService.GetDeviceSpec(new PhysicalDiskInfo { DeviceId = "1", BusType = "NVMe" });
        var usb = SmartTestService.GetDeviceSpec(new PhysicalDiskInfo { DeviceId = "2", BusType = "USB" });

        Assert.True(sata.IsSupported);
        Assert.Equal("/dev/pd0", sata.DevicePath);
        Assert.Equal("", sata.DeviceTypeArgument);

        Assert.True(nvme.IsSupported);
        Assert.Equal("/dev/pd1", nvme.DevicePath);
        Assert.Equal("-d nvme", nvme.DeviceTypeArgument);

        Assert.True(usb.IsSupported);
        Assert.Equal("/dev/pd2", usb.DevicePath);
        Assert.Equal("-d sat", usb.DeviceTypeArgument);
        Assert.Equal("Warning", usb.Status);
        Assert.Contains("USB bridge", usb.Detail);
    }

    [Fact]
    public async Task GetSelfTestCapabilityAsync_RejectsUnsupportedDeviceId()
    {
        var runner = new FakeRunner()
            .Respond("smartctl", "--version", "smartctl 7.5")
            .Respond("where.exe", "smartctl", "C:\\Tools\\smartctl.exe");

        var capability = await SmartTestService.GetSelfTestCapabilityAsync(
            new PhysicalDiskInfo { DeviceId = "PhysicalDrive0", BusType = "SATA" },
            runner,
            new TestLog());

        Assert.False(capability.CanRunSelfTest);
        Assert.Equal("UnsupportedDevice", capability.Status);
        Assert.Contains("Cannot map disk DeviceId", capability.Detail);
    }

    [Fact]
    public async Task StartTestAsync_UsesSelectedDeviceMode()
    {
        var runner = new FakeRunner()
            .Respond("smartctl", "--version", "smartctl 7.5")
            .Respond("where.exe", "smartctl", "C:\\Tools\\smartctl.exe")
            .Respond("smartctl", "-d nvme -t short /dev/pd3", "Testing has begun.\nPlease wait 2 minutes for test to complete after polling.");

        var result = await SmartTestService.StartTestAsync(
            new PhysicalDiskInfo { DeviceId = "3", BusType = "NVMe" },
            SmartTestType.Short,
            runner,
            new TestLog());

        Assert.True(result.Started);
        Assert.Contains(runner.Calls, call => call.FileName == "smartctl" && call.Arguments == "-d nvme -t short /dev/pd3");
    }

    private sealed class TestLog : IActivityLog
    {
        public void Log(string message) { }
    }

    private sealed class FakeRunner : IProcessRunner
    {
        private readonly Dictionary<(string FileName, string Arguments), object> _responses = new();

        public List<(string FileName, string Arguments)> Calls { get; } = new();

        public FakeRunner Respond(string fileName, string arguments, string output)
        {
            _responses[(fileName, arguments)] = output;
            return this;
        }

        public FakeRunner Throw(string fileName, string arguments, Exception exception)
        {
            _responses[(fileName, arguments)] = exception;
            return this;
        }

        public Task<string> RunDiskpartAsync(string script, IActivityLog? log = null, CancellationToken ct = default) =>
            Task.FromResult("");

        public Task<string> RunPowerShellAsync(string command, IActivityLog? log = null, CancellationToken ct = default) =>
            Task.FromResult("");

        public Task<string> RunExeAsync(
            string fileName,
            string arguments,
            IActivityLog? log = null,
            bool ignoreStderrOnSuccess = false,
            CancellationToken ct = default)
        {
            Calls.Add((fileName, arguments));
            if (!_responses.TryGetValue((fileName, arguments), out var response))
                throw new InvalidOperationException($"Unexpected command: {fileName} {arguments}");
            if (response is Exception ex)
                throw ex;
            return Task.FromResult((string)response);
        }
    }
}
