using LibreHardwareMonitor.Hardware;

namespace PartitionPilot;

public static class SmartQueryService
{
    private static readonly object OpenLock = new();
    private static Computer? _computer;

    /// <summary>
    /// One LibreHardwareMonitor instance for the life of the process.
    /// <para>
    /// Opening one is not cheap: it enumerates every storage device and allocates an executable code
    /// buffer for generated CPUID/RDTSC instructions, neither of which the storage-only path here needs.
    /// Constructing one per query meant the temperature monitor paid that cost once per disk every thirty
    /// seconds. Refreshing an already-open device with Update() is the supported way to get new readings.
    /// </para>
    /// </summary>
    private static Computer GetComputer()
    {
        lock (OpenLock)
        {
            if (_computer is not null)
                return _computer;

            var computer = new Computer { IsStorageEnabled = true };
            computer.Open();
            _computer = computer;
            return computer;
        }
    }

    /// <summary>Closes the shared instance. Called on shutdown; safe to call when nothing was opened.</summary>
    public static void Shutdown()
    {
        lock (OpenLock)
        {
            try { _computer?.Close(); }
            catch { /* nothing useful to do while tearing down */ }
            _computer = null;
        }
    }

    public static SmartData? QueryDisk(int diskNumber, IActivityLog? log = null)
    {
        try
        {
            // Serialised because the hardware collection and each device's sensors are shared state, and
            // the temperature monitor polls while the health tab can be refreshing.
            lock (OpenLock)
            {
            var computer = GetComputer();

            var storageDevices = computer.Hardware
                .Where(h => h.HardwareType == HardwareType.Storage)
                .ToList();

            var device = storageDevices.FirstOrDefault(hardware =>
                GetStorageDiskNumber(hardware.Identifier.ToString()) == diskNumber);

            if (device is null)
            {
                log?.Log($"LibreHardwareMonitor did not report a storage device for Windows disk {diskNumber}.");
                return null;
            }

            device.Update();

            var data = new SmartData();
            var attributes = new List<SmartAttribute>();

            foreach (var sensor in device.Sensors)
            {
                if (sensor.Value is null) continue;

                switch (sensor.SensorType)
                {
                    case SensorType.Temperature:
                        data.Temperature ??= (int)sensor.Value.Value;
                        break;

                    case SensorType.Level when sensor.Name.Contains("Available Spare", StringComparison.OrdinalIgnoreCase):
                        data.NvmeAvailableSpare = (int)sensor.Value.Value;
                        break;

                    case SensorType.Level when sensor.Name.Contains("Percentage Used", StringComparison.OrdinalIgnoreCase)
                                            || sensor.Name.Contains("Used", StringComparison.OrdinalIgnoreCase):
                        data.Wear ??= (int)sensor.Value.Value;
                        break;

                    case SensorType.Level when sensor.Name.Contains("Remaining Life", StringComparison.OrdinalIgnoreCase):
                        data.Wear ??= 100 - (int)sensor.Value.Value;
                        break;

                    case SensorType.Data when sensor.Name.Contains("Written", StringComparison.OrdinalIgnoreCase):
                        data.TotalBytesWritten = (long)(sensor.Value.Value * 1024L * 1024L * 1024L);
                        break;

                    case SensorType.Data when sensor.Name.Contains("Read", StringComparison.OrdinalIgnoreCase):
                        data.TotalBytesRead = (long)(sensor.Value.Value * 1024L * 1024L * 1024L);
                        break;

                    case SensorType.Factor when sensor.Name.Contains("Reallocated", StringComparison.OrdinalIgnoreCase):
                        data.ReallocatedSectors = (long)sensor.Value.Value;
                        attributes.Add(new SmartAttribute { Id = 5, Name = sensor.Name, RawValue = (long)sensor.Value.Value });
                        break;

                    case SensorType.Factor when sensor.Name.Contains("Pending", StringComparison.OrdinalIgnoreCase):
                        data.PendingSectors = (long)sensor.Value.Value;
                        attributes.Add(new SmartAttribute { Id = 197, Name = sensor.Name, RawValue = (long)sensor.Value.Value });
                        break;

                    case SensorType.Factor when sensor.Name.Contains("Power Cycle", StringComparison.OrdinalIgnoreCase):
                        data.PowerCycleCount = (long)sensor.Value.Value;
                        attributes.Add(new SmartAttribute { Id = 12, Name = sensor.Name, RawValue = (long)sensor.Value.Value });
                        break;

                    case SensorType.Factor when sensor.Name.Contains("Power-On", StringComparison.OrdinalIgnoreCase)
                                             || sensor.Name.Contains("Power On", StringComparison.OrdinalIgnoreCase):
                        data.PowerOnHours ??= (long)sensor.Value.Value;
                        attributes.Add(new SmartAttribute { Id = 9, Name = sensor.Name, RawValue = (long)sensor.Value.Value });
                        break;

                    default:
                        if (sensor.Name.Contains("Media Error", StringComparison.OrdinalIgnoreCase))
                            data.NvmeMediaErrors = (long)sensor.Value.Value;

                        attributes.Add(new SmartAttribute { Name = sensor.Name, RawValue = (long)sensor.Value.Value });
                        break;
                }
            }

            data.AllAttributes = attributes;
            log?.Log($"LibreHardwareMonitor returned {attributes.Count} SMART attribute(s) for Windows disk {diskNumber}.");
            return data;
            }
        }
        catch (Exception ex)
        {
            log?.Log($"LibreHardwareMonitor SMART query failed: {ex.Message}");

            // A failure may have left the shared instance unusable, so drop it and let the next query
            // open a fresh one rather than failing forever.
            Shutdown();
            return null;
        }
    }

    public static int? GetStorageDiskNumber(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        var separatorIndex = identifier.LastIndexOf('/');
        if (separatorIndex < 0 || separatorIndex == identifier.Length - 1)
            return null;

        return int.TryParse(identifier[(separatorIndex + 1)..], out var diskNumber) && diskNumber >= 0
            ? diskNumber
            : null;
    }
}
