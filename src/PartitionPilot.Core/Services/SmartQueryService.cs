using LibreHardwareMonitor.Hardware;

namespace PartitionPilot;

public static class SmartQueryService
{
    public static SmartData? QueryDisk(int diskIndex, IActivityLog? log = null)
    {
        Computer? computer = null;
        try
        {
            computer = new Computer { IsStorageEnabled = true };
            computer.Open();

            var storageDevices = computer.Hardware
                .Where(h => h.HardwareType == HardwareType.Storage)
                .ToList();

            if (diskIndex >= storageDevices.Count)
                return null;

            var device = storageDevices[diskIndex];
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
            log?.Log($"LibreHardwareMonitor returned {attributes.Count} SMART attribute(s) for device index {diskIndex}.");
            return data;
        }
        catch (Exception ex)
        {
            log?.Log($"LibreHardwareMonitor SMART query failed: {ex.Message}");
            return null;
        }
        finally
        {
            computer?.Close();
        }
    }
}
