namespace PartitionPilot;

public enum SmartTestType { Short, Extended }

public class SmartTestResult
{
    public bool Started { get; set; }
    public string Message { get; set; } = "";
    public string? EstimatedDuration { get; set; }
}

public static class SmartTestService
{
    public static async Task<SmartTestResult> StartTestAsync(
        int diskNumber, SmartTestType testType, IProcessRunner runner, IActivityLog log)
    {
        var testFlag = testType == SmartTestType.Short ? "short" : "long";
        var devicePath = $"/dev/pd{diskNumber}";

        log.Log($"Starting SMART {testFlag} self-test on disk {diskNumber}...");

        try
        {
            var output = await runner.RunExeAsync("smartctl", $"-t {testFlag} {devicePath}", log);
            var started = output.Contains("Testing has begun", StringComparison.OrdinalIgnoreCase) ||
                          output.Contains("self-test has begun", StringComparison.OrdinalIgnoreCase);

            string? duration = null;
            foreach (var line in output.Split('\n'))
            {
                if (line.Contains("complete after", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("estimated", StringComparison.OrdinalIgnoreCase))
                {
                    duration = line.Trim();
                    break;
                }
            }

            log.Log(started
                ? $"SMART {testFlag} self-test started on disk {diskNumber}"
                : $"SMART self-test may not have started: {output.Trim()}");

            return new SmartTestResult
            {
                Started = started,
                Message = started ? $"SMART {testFlag} self-test started." : output.Trim(),
                EstimatedDuration = duration
            };
        }
        catch (Exception ex)
        {
            log.Log($"SMART self-test failed: {ex.Message}");
            return new SmartTestResult
            {
                Started = false,
                Message = $"smartctl not available or test failed: {ex.Message}"
            };
        }
    }

    public static async Task<string> GetTestStatusAsync(int diskNumber, IProcessRunner runner, IActivityLog log)
    {
        var devicePath = $"/dev/pd{diskNumber}";

        try
        {
            var output = await runner.RunExeAsync("smartctl", $"-l selftest {devicePath}", log,
                ignoreStderrOnSuccess: true);
            return output;
        }
        catch (Exception ex)
        {
            return $"Could not read self-test log: {ex.Message}";
        }
    }

    public static async Task<bool> IsSmartctlAvailableAsync(IProcessRunner runner, IActivityLog log)
    {
        try
        {
            await runner.RunExeAsync("smartctl", "--version", log, ignoreStderrOnSuccess: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
