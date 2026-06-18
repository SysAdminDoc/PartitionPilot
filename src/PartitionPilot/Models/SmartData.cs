namespace PartitionPilot;

public enum HealthStatus { Good, Warning, Critical, Unknown }

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

    public HealthStatus Health
    {
        get
        {
            if (Wear is not null && Wear >= 95) return HealthStatus.Critical;
            if (Temperature is not null && Temperature >= 65) return HealthStatus.Critical;
            if (Wear is not null && Wear >= 85) return HealthStatus.Warning;
            if (Temperature is not null && Temperature >= 55) return HealthStatus.Warning;
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
            if (Temperature is not null && Temperature >= 65) return $"Temperature critically high ({Temperature}°C)";
            if (Wear is not null && Wear >= 85) return $"SSD wear indicator at {Wear}% — consider replacement planning";
            if (Temperature is not null && Temperature >= 55) return $"Temperature elevated ({Temperature}°C)";
            if (ReadErrorsTotal is not null && ReadErrorsTotal > 0 && ReadErrorsCorrected != ReadErrorsTotal)
                return $"Uncorrected read errors detected ({ReadErrorsTotal - (ReadErrorsCorrected ?? 0)})";
            if (WriteErrorsTotal is not null && WriteErrorsTotal > 0 && WriteErrorsCorrected != WriteErrorsTotal)
                return $"Uncorrected write errors detected ({WriteErrorsTotal - (WriteErrorsCorrected ?? 0)})";
            if (Health == HealthStatus.Unknown) return "SMART data not available for this drive";
            return "All monitored attributes within normal range";
        }
    }
}
