namespace PartitionPilot;

public enum HealthStatus { Good, Warning, Critical, Unknown }

public class SmartAttribute
{
    public byte Id { get; set; }
    public string Name { get; set; } = "";
    public int Current { get; set; }
    public int Worst { get; set; }
    public long RawValue { get; set; }
    public string RawDisplay => RawValue.ToString("N0");
    public string DisplayName => SmartAttributeMetadataService.DescribeAttribute(this).Name;
    public string AdvisorySeverity => SmartAttributeMetadataService.DescribeAttribute(this).Severity;
    public string AdvisoryText => SmartAttributeMetadataService.DescribeAttribute(this).Explanation;
    public string Recommendation => SmartAttributeMetadataService.DescribeAttribute(this).Recommendation;
    public string MetadataVersion => SmartAttributeMetadataService.MetadataVersion;
    public bool HasCuratedMetadata => SmartAttributeMetadataService.DescribeAttribute(this).HasCuratedMetadata;
}

public class SmartAdvisory
{
    public string Source { get; set; } = "";
    public string Name { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public long? RawValue { get; set; }
    public string MetadataVersion { get; set; } = SmartAttributeMetadataService.MetadataVersion;
}

public class SmartData
{
    public int? Temperature { get; set; }
    public int? Wear { get; set; }
    public long? PowerOnHours { get; set; }
    public long? ReadErrorsTotal { get; set; }
    public long? ReadErrorsCorrected { get; set; }
    public long? WriteErrorsTotal { get; set; }
    public long? WriteErrorsCorrected { get; set; }
    public long? ReadLatencyMax { get; set; }
    public long? WriteLatencyMax { get; set; }

    public long? ReallocatedSectors { get; set; }
    public long? PendingSectors { get; set; }
    public long? PowerCycleCount { get; set; }
    public long? TotalBytesWritten { get; set; }
    public long? TotalBytesRead { get; set; }
    public int? NvmeAvailableSpare { get; set; }
    public long? NvmeMediaErrors { get; set; }
    public long? NvmeUnsafeShutdowns { get; set; }
    public long? NvmeControllerBusyMinutes { get; set; }
    public long? NvmeErrorLogEntries { get; set; }
    public byte? NvmeCriticalWarning { get; set; }

    public List<SmartAttribute> AllAttributes { get; set; } = new();
    public string MetadataVersion => SmartAttributeMetadataService.MetadataVersion;
    public IReadOnlyList<SmartAdvisory> Advisories => SmartAttributeMetadataService.BuildAdvisories(this);

    public List<string> CriticalWarningFlags
    {
        get
        {
            if (NvmeCriticalWarning is null or 0) return new();
            var flags = new List<string>();
            var w = NvmeCriticalWarning.Value;
            if ((w & 0x01) != 0) flags.Add("Available spare low");
            if ((w & 0x02) != 0) flags.Add("Temperature threshold exceeded");
            if ((w & 0x04) != 0) flags.Add("Reliability degraded");
            if ((w & 0x08) != 0) flags.Add("Read-only mode");
            if ((w & 0x10) != 0) flags.Add("Volatile memory backup failed");
            return flags;
        }
    }

    public HealthStatus Health
    {
        get
        {
            if (Wear is not null && Wear >= 95) return HealthStatus.Critical;
            if (Temperature is not null && Temperature >= 65) return HealthStatus.Critical;
            if (ReallocatedSectors is not null && ReallocatedSectors > 100) return HealthStatus.Critical;
            if (NvmeAvailableSpare is not null && NvmeAvailableSpare <= 5) return HealthStatus.Critical;
            if (Wear is not null && Wear >= 85) return HealthStatus.Warning;
            if (Temperature is not null && Temperature >= 55) return HealthStatus.Warning;
            if (ReallocatedSectors is not null && ReallocatedSectors > 0) return HealthStatus.Warning;
            if (PendingSectors is not null && PendingSectors > 0) return HealthStatus.Warning;
            if (NvmeAvailableSpare is not null && NvmeAvailableSpare <= 20) return HealthStatus.Warning;
            if (NvmeMediaErrors is not null && NvmeMediaErrors > 0) return HealthStatus.Warning;
            if (ReadErrorsTotal is not null && ReadErrorsTotal > 0 && ReadErrorsCorrected != ReadErrorsTotal)
                return HealthStatus.Warning;
            if (WriteErrorsTotal is not null && WriteErrorsTotal > 0 && WriteErrorsCorrected != WriteErrorsTotal)
                return HealthStatus.Warning;
            if (Temperature is null && Wear is null && PowerOnHours is null)
                return HealthStatus.Unknown;
            return HealthStatus.Good;
        }
    }

    public string HealthReason
    {
        get
        {
            if (Wear is not null && Wear >= 95) return $"SSD wear indicator at {Wear}% — nearing estimated wear limit";
            if (Temperature is not null && Temperature >= 65) return $"Temperature critically high ({Temperature} C)";
            if (ReallocatedSectors is not null && ReallocatedSectors > 100) return $"Critical: {ReallocatedSectors} reallocated sectors";
            if (NvmeAvailableSpare is not null && NvmeAvailableSpare <= 5) return $"NVMe available spare critically low ({NvmeAvailableSpare}%)";
            if (Wear is not null && Wear >= 85) return $"SSD wear indicator at {Wear}% — consider replacement planning";
            if (Temperature is not null && Temperature >= 55) return $"Temperature elevated ({Temperature} C)";
            if (ReallocatedSectors is not null && ReallocatedSectors > 0) return $"Warning: {ReallocatedSectors} reallocated sector(s) — early sign of surface degradation";
            if (PendingSectors is not null && PendingSectors > 0) return $"Warning: {PendingSectors} pending sector(s) — awaiting reallocation";
            if (NvmeAvailableSpare is not null && NvmeAvailableSpare <= 20) return $"NVMe available spare getting low ({NvmeAvailableSpare}%)";
            if (NvmeMediaErrors is not null && NvmeMediaErrors > 0) return $"NVMe media errors detected ({NvmeMediaErrors})";
            if (ReadErrorsTotal is not null && ReadErrorsTotal > 0 && ReadErrorsCorrected != ReadErrorsTotal)
                return $"Uncorrected read errors detected ({ReadErrorsTotal - (ReadErrorsCorrected ?? 0)})";
            if (WriteErrorsTotal is not null && WriteErrorsTotal > 0 && WriteErrorsCorrected != WriteErrorsTotal)
                return $"Uncorrected write errors detected ({WriteErrorsTotal - (WriteErrorsCorrected ?? 0)})";
            if (Health == HealthStatus.Unknown) return "SMART data not available for this drive";
            return "All monitored attributes within normal range";
        }
    }
}
