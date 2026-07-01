using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace PartitionPilot;

public class DiskHealthViewModel : ViewModelBase
{
    private readonly IWmiDiskService _wmiService;
    private readonly IProcessRunner _runner;
    private readonly ActivityLog _log;
    private readonly SmartHistoryService _history = new();
    private readonly TemperatureMonitorService _tempMonitor;
    private readonly DiskPerfCounterService _perfCounters = new();
    private CancellationTokenSource? _healthCts;

    public ObservableCollection<PhysicalDiskInfo> PhysicalDisks { get; } = new();
    public ObservableCollection<AlignmentInfo> AlignmentEntries { get; } = new();
    public ObservableCollection<TemperatureAlert> TemperatureAlerts { get; } = new();

    private PhysicalDiskInfo? _selectedDisk;
    public PhysicalDiskInfo? SelectedDisk
    {
        get => _selectedDisk;
        set
        {
            if (SetProperty(ref _selectedDisk, value))
            {
                OnPropertyChanged(nameof(DiskSizeText));
                OnPropertyChanged(nameof(SectorSizeText));
                LoadRatedTbw();
                OnPropertyChanged(nameof(RatedTbwTB));
                _healthCts?.Cancel();
                _healthCts?.Dispose();
                _healthCts = new CancellationTokenSource();
                OnPropertyChanged(nameof(CanRunSmartSelfTest));
                _ = RefreshSmartSelfTestCapabilityAsync(_healthCts.Token);
                _ = LoadHealthDataAsync(_healthCts.Token);
            }
        }
    }

    private SmartData? _smart;
    public SmartData? Smart
    {
        get => _smart;
        set
        {
            if (SetProperty(ref _smart, value))
            {
                OnPropertyChanged(nameof(HasSmartData));
                OnPropertyChanged(nameof(TemperatureText));
                OnPropertyChanged(nameof(WearText));
                OnPropertyChanged(nameof(PowerOnText));
                OnPropertyChanged(nameof(ReadErrorsText));
                OnPropertyChanged(nameof(WriteErrorsText));
                OnPropertyChanged(nameof(ReadLatencyText));
                OnPropertyChanged(nameof(WriteLatencyText));
                OnPropertyChanged(nameof(ReallocatedSectorsText));
                OnPropertyChanged(nameof(PendingSectorsText));
                OnPropertyChanged(nameof(PowerCycleText));
                OnPropertyChanged(nameof(TotalWrittenText));
                OnPropertyChanged(nameof(TotalReadText));
                OnPropertyChanged(nameof(NvmeAvailableSpareText));
                OnPropertyChanged(nameof(NvmeMediaErrorsText));
                OnPropertyChanged(nameof(NvmeUnsafeShutdownsText));
                OnPropertyChanged(nameof(NvmeControllerBusyText));
                OnPropertyChanged(nameof(NvmeErrorLogText));
                OnPropertyChanged(nameof(NvmeCriticalWarningText));
                OnPropertyChanged(nameof(IsNvmeDrive));
                OnPropertyChanged(nameof(HasEnduranceData));
                OnPropertyChanged(nameof(EnduranceText));
                OnPropertyChanged(nameof(EndurancePercent));
                OnPropertyChanged(nameof(HasExtendedSmartData));
                OnPropertyChanged(nameof(SmartAttributes));
                OnPropertyChanged(nameof(HasSmartAdvisories));
                OnPropertyChanged(nameof(SmartAdvisories));
                OnPropertyChanged(nameof(SmartMetadataVersionText));
                OnPropertyChanged(nameof(HealthStatusText));
                OnPropertyChanged(nameof(HealthReasonText));
            }
        }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRunSmartSelfTest));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    // ──────────────────────── Display Properties ────────────────────────

    public bool HasSmartData => Smart is not null;

    public string DiskSizeText => SelectedDisk is not null ? SizeUtil.Format(SelectedDisk.Size) : "";

    public string SectorSizeText => SelectedDisk is not null
        ? $"Logical: {SelectedDisk.LogicalSectorSize} / Physical: {SelectedDisk.PhysicalSectorSize}"
        : "";

    public string TemperatureText => Smart?.Temperature is not null ? $"{Smart.Temperature} C" : "N/A";

    public string WearText => Smart?.Wear is not null ? $"{Smart.Wear}% used" : "N/A";

    public string PowerOnText => Smart?.PowerOnHours is not null ? $"{Smart.PowerOnHours:N0} hours" : "N/A";

    public string ReadErrorsText
    {
        get
        {
            if (Smart?.ReadErrorsTotal is null) return "N/A";
            var corrected = Smart.ReadErrorsCorrected?.ToString() ?? "?";
            return $"{Smart.ReadErrorsTotal} total ({corrected} corrected)";
        }
    }

    public string WriteErrorsText
    {
        get
        {
            if (Smart?.WriteErrorsTotal is null) return "N/A";
            var corrected = Smart.WriteErrorsCorrected?.ToString() ?? "?";
            return $"{Smart.WriteErrorsTotal} total ({corrected} corrected)";
        }
    }

    public string ReadLatencyText => Smart?.ReadLatencyMax is not null ? $"{Smart.ReadLatencyMax} ms" : "N/A";

    public string WriteLatencyText => Smart?.WriteLatencyMax is not null ? $"{Smart.WriteLatencyMax} ms" : "N/A";

    public string ReallocatedSectorsText => Smart?.ReallocatedSectors is not null ? Smart.ReallocatedSectors.Value.ToString("N0") : "N/A";

    public string PendingSectorsText => Smart?.PendingSectors is not null ? Smart.PendingSectors.Value.ToString("N0") : "N/A";

    public string PowerCycleText => Smart?.PowerCycleCount is not null ? Smart.PowerCycleCount.Value.ToString("N0") : "N/A";

    public string TotalWrittenText => Smart?.TotalBytesWritten is not null ? SizeUtil.Format(Smart.TotalBytesWritten.Value) : "N/A";

    public string TotalReadText => Smart?.TotalBytesRead is not null ? SizeUtil.Format(Smart.TotalBytesRead.Value) : "N/A";

    public string NvmeAvailableSpareText => Smart?.NvmeAvailableSpare is not null ? $"{Smart.NvmeAvailableSpare}%" : "N/A";

    public string NvmeMediaErrorsText => Smart?.NvmeMediaErrors is not null ? Smart.NvmeMediaErrors.Value.ToString("N0") : "N/A";

    public string NvmeUnsafeShutdownsText => Smart?.NvmeUnsafeShutdowns is not null ? Smart.NvmeUnsafeShutdowns.Value.ToString("N0") : "N/A";

    public string NvmeControllerBusyText => Smart?.NvmeControllerBusyMinutes is not null ? $"{Smart.NvmeControllerBusyMinutes:N0} min" : "N/A";

    public string NvmeErrorLogText => Smart?.NvmeErrorLogEntries is not null ? Smart.NvmeErrorLogEntries.Value.ToString("N0") : "N/A";

    public string NvmeCriticalWarningText
    {
        get
        {
            var flags = Smart?.CriticalWarningFlags ?? new();
            return flags.Count > 0 ? string.Join(", ", flags) : "None";
        }
    }

    public bool IsNvmeDrive => SelectedDisk?.BusType == "NVMe";

    public string EnduranceText
    {
        get
        {
            if (Smart?.TotalBytesWritten is null) return "";
            var writtenTB = Smart.TotalBytesWritten.Value / (1024.0 * 1024 * 1024 * 1024);
            if (RatedTbwTB <= 0) return $"{writtenTB:F1} TB written (set rated TBW for endurance gauge)";
            var pct = writtenTB / RatedTbwTB * 100;
            return $"{writtenTB:F1} TB written of {RatedTbwTB:F0} TB rated ({pct:F1}% consumed)";
        }
    }

    public double EndurancePercent
    {
        get
        {
            if (Smart?.TotalBytesWritten is null || RatedTbwTB <= 0) return 0;
            var writtenTB = Smart.TotalBytesWritten.Value / (1024.0 * 1024 * 1024 * 1024);
            return Math.Min(100, writtenTB / RatedTbwTB * 100);
        }
    }

    private double _ratedTbwTB;
    public double RatedTbwTB
    {
        get => _ratedTbwTB;
        set
        {
            if (SetProperty(ref _ratedTbwTB, value))
            {
                OnPropertyChanged(nameof(EnduranceText));
                OnPropertyChanged(nameof(EndurancePercent));
                SaveRatedTbw();
            }
        }
    }

    public bool HasEnduranceData => Smart?.TotalBytesWritten is not null;

    private void LoadRatedTbw()
    {
        if (SelectedDisk is null) return;
        var path = GetRatedTbwPath(SelectedDisk.DeviceId);
        if (File.Exists(path) && double.TryParse(File.ReadAllText(path).Trim(), out var val))
            _ratedTbwTB = val;
        else
            _ratedTbwTB = 0;
    }

    private void SaveRatedTbw()
    {
        if (SelectedDisk is null) return;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PartitionPilot", "settings");
        Directory.CreateDirectory(dir);
        File.WriteAllText(GetRatedTbwPath(SelectedDisk.DeviceId), _ratedTbwTB.ToString("F0"));
    }

    private static string GetRatedTbwPath(string deviceId) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PartitionPilot", "settings", $"tbw_disk{deviceId}.txt");

    public bool HasExtendedSmartData => Smart?.AllAttributes.Count > 0;

    public IReadOnlyList<SmartAttribute> SmartAttributes => Smart?.AllAttributes ?? new List<SmartAttribute>();

    public bool HasSmartAdvisories => Smart?.Advisories.Count > 0;

    public IReadOnlyList<SmartAdvisory> SmartAdvisories => Smart?.Advisories ?? new List<SmartAdvisory>();

    public string SmartMetadataVersionText => Smart is null ? "" : $"SMART metadata {Smart.MetadataVersion}";

    public ObservableCollection<SmartTrend> Trends { get; } = new();
    public bool HasTrends => Trends.Count > 0;

    private int _historyCount;
    public int HistoryCount
    {
        get => _historyCount;
        set => SetProperty(ref _historyCount, value);
    }

    public string HistoryCountText => HistoryCount switch
    {
        0 => "No history",
        1 => "1 reading",
        _ => $"{HistoryCount} readings"
    };

    public string HealthStatusText => Smart?.Health switch
    {
        HealthStatus.Good => "Good",
        HealthStatus.Warning => "Warning",
        HealthStatus.Critical => "Critical",
        _ => "Unknown"
    };

    public string HealthReasonText => Smart?.HealthReason ?? "No SMART data available";

    private bool _isMonitoring;
    public bool IsMonitoring
    {
        get => _isMonitoring;
        set
        {
            if (SetProperty(ref _isMonitoring, value))
            {
                OnPropertyChanged(nameof(MonitorButtonText));
                OnPropertyChanged(nameof(HasTemperatureAlerts));
            }
        }
    }

    public string MonitorButtonText => IsMonitoring ? "Stop Monitoring" : "Start Monitoring";
    public bool HasTemperatureAlerts => TemperatureAlerts.Count > 0;

    private string _liveTemperatureText = "";
    public string LiveTemperatureText
    {
        get => _liveTemperatureText;
        set => SetProperty(ref _liveTemperatureText, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand ToggleMonitorCommand { get; }
    public ICommand RunShortTestCommand { get; }
    public ICommand RunExtendedTestCommand { get; }
    public ICommand ExportReportCommand { get; }
    public ICommand TogglePerfCountersCommand { get; }

    private bool _isPerfMonitoring;
    public bool IsPerfMonitoring
    {
        get => _isPerfMonitoring;
        set
        {
            if (SetProperty(ref _isPerfMonitoring, value))
                OnPropertyChanged(nameof(PerfButtonText));
        }
    }

    public string PerfButtonText => IsPerfMonitoring ? "Stop I/O Monitor" : "Start I/O Monitor";

    private string _perfText = "";
    public string PerfText
    {
        get => _perfText;
        set => SetProperty(ref _perfText, value);
    }

    public bool HasPerfData => !string.IsNullOrEmpty(PerfText);

    private string _selfTestStatus = "";
    public string SelfTestStatus
    {
        get => _selfTestStatus;
        set => SetProperty(ref _selfTestStatus, value);
    }

    private SmartctlCapability _smartSelfTestCapability = new()
    {
        CanRunSelfTest = false,
        Status = "NotChecked",
        Detail = "Select a disk to check SMART self-test support."
    };

    public SmartctlCapability SmartSelfTestCapability
    {
        get => _smartSelfTestCapability;
        private set
        {
            if (SetProperty(ref _smartSelfTestCapability, value))
            {
                OnPropertyChanged(nameof(CanRunSmartSelfTest));
                OnPropertyChanged(nameof(SmartSelfTestSupportText));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanRunSmartSelfTest => SelectedDisk is not null && SmartSelfTestCapability.CanRunSelfTest && !IsBusy;

    public string SmartSelfTestSupportText
    {
        get
        {
            var detail = SmartSelfTestCapability.Detail;
            if (!string.IsNullOrWhiteSpace(SmartSelfTestCapability.Remediation) &&
                !SmartSelfTestCapability.CanRunSelfTest)
                detail = $"{detail} {SmartSelfTestCapability.Remediation}".Trim();
            return string.IsNullOrWhiteSpace(detail) ? "SMART self-test support not checked." : detail;
        }
    }

    public DiskHealthViewModel(IWmiDiskService wmiService, IProcessRunner runner, ActivityLog log)
    {
        _wmiService = wmiService;
        _runner = runner;
        _log = log;
        _tempMonitor = new TemperatureMonitorService(wmiService, log);

        _tempMonitor.AlertRaised += OnTemperatureAlert;
        _tempMonitor.TemperaturesUpdated += OnTemperaturesUpdated;

        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        ToggleMonitorCommand = new WpfRelayCommand(_ => ToggleMonitoring());
        RunShortTestCommand = new AsyncRelayCommand(_ => RunSelfTestAsync(SmartTestType.Short),
            _ => CanRunSmartSelfTest);
        RunExtendedTestCommand = new AsyncRelayCommand(_ => RunSelfTestAsync(SmartTestType.Extended),
            _ => CanRunSmartSelfTest);
        ExportReportCommand = new AsyncRelayCommand(_ => ExportReportAsync(),
            _ => SelectedDisk is not null && Smart is not null);
        TogglePerfCountersCommand = new WpfRelayCommand(_ => TogglePerfCounters());

        _perfCounters.Updated += OnPerfUpdated;
    }

    private void TogglePerfCounters()
    {
        if (IsPerfMonitoring)
        {
            _perfCounters.Stop();
            IsPerfMonitoring = false;
            PerfText = "";
            OnPropertyChanged(nameof(HasPerfData));
            _log.Log("Disk I/O monitoring stopped.");
        }
        else
        {
            var diskNums = PhysicalDisks.Select(d => int.TryParse(d.DeviceId, out var n) ? n : -1).Where(n => n >= 0);
            _perfCounters.Start(diskNums);
            IsPerfMonitoring = true;
            OnPropertyChanged(nameof(HasPerfData));
            _log.Log("Disk I/O monitoring started.");
        }
    }

    private void OnPerfUpdated(List<DiskPerfSnapshot> snapshots)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var s in snapshots)
        {
            sb.AppendLine($"Disk {s.DiskNumber}: " +
                $"Read {s.ReadMBps:F1} MB/s ({s.ReadIOPS:F0} IOPS)  " +
                $"Write {s.WriteMBps:F1} MB/s ({s.WriteIOPS:F0} IOPS)  " +
                $"Queue {s.QueueLength:F1}  " +
                $"Latency R:{s.AvgReadLatencyMs:F1}ms W:{s.AvgWriteLatencyMs:F1}ms");
        }
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            PerfText = sb.ToString().TrimEnd();
            OnPropertyChanged(nameof(HasPerfData));
        });
    }

    private async Task ExportReportAsync()
    {
        if (SelectedDisk is null || Smart is null) return;

        var deviceId = SelectedDisk.DeviceId;
        var readings = await _history.GetHistoryAsync(deviceId);
        var trends = SmartHistoryService.AnalyzeTrends(readings);
        var alignments = await _wmiService.GetAlignmentAuditAsync();

        var html = SmartHistoryService.FormatHtmlReport(SelectedDisk, Smart, readings, trends, alignments);

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PartitionPilot", "reports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"smart_report_disk{deviceId}_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.html");
        await File.WriteAllTextAsync(path, html);

        _log.Log($"SMART report exported to: {path}");

        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { }
    }

    private async Task RunSelfTestAsync(SmartTestType testType)
    {
        var selectedDisk = SelectedDisk;
        if (selectedDisk is null) return;
        if (!SmartSelfTestCapability.CanRunSelfTest)
        {
            SelfTestStatus = SmartSelfTestSupportText;
            return;
        }

        IsBusy = true;
        SelfTestStatus = $"Starting {testType} self-test...";

        try
        {
            var result = await SmartTestService.StartTestAsync(selectedDisk, testType, _runner, _log);
            SelfTestStatus = result.Started
                ? $"{testType} test started. {result.EstimatedDuration ?? ""}"
                : result.Message;
        }
        catch (Exception ex)
        {
            SelfTestStatus = $"Self-test failed: {ex.Message}";
            _log.Log($"SMART self-test error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshAsync()
    {
        await LoadPhysicalDisksAsync();
        await RefreshSmartSelfTestCapabilityAsync(CancellationToken.None);
        await LoadHealthDataAsync(CancellationToken.None);
    }

    private async Task RefreshSmartSelfTestCapabilityAsync(CancellationToken ct)
    {
        var selectedDisk = SelectedDisk;
        if (selectedDisk is null)
        {
            SmartSelfTestCapability = new SmartctlCapability
            {
                CanRunSelfTest = false,
                Status = "NoDisk",
                Detail = "Select a physical disk before running SMART self-tests."
            };
            return;
        }

        SmartSelfTestCapability = new SmartctlCapability
        {
            CanRunSelfTest = false,
            Status = "Checking",
            Detail = "Checking smartctl support..."
        };

        try
        {
            var capability = await SmartTestService.GetSelfTestCapabilityAsync(selectedDisk, _runner, _log);
            ct.ThrowIfCancellationRequested();
            if (SelectedDisk?.DeviceId == selectedDisk.DeviceId)
                SmartSelfTestCapability = capability;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SmartSelfTestCapability = new SmartctlCapability
            {
                CanRunSelfTest = false,
                Status = "Error",
                Detail = $"Could not check smartctl support: {ex.Message}",
                Remediation = "Run diagnostics and verify smartctl is installed."
            };
        }
    }

    private async Task LoadPhysicalDisksAsync()
    {
        IsBusy = true;
        try
        {
            _log.Log("Loading physical disk information...");
            var disks = await _wmiService.GetPhysicalDisksAsync();

            Application.Current.Dispatcher.Invoke(() =>
            {
                PhysicalDisks.Clear();
                foreach (var d in disks)
                    PhysicalDisks.Add(d);
            });

            _log.Log($"Found {disks.Count} physical disk(s).");

            if (PhysicalDisks.Count > 0 && SelectedDisk is null)
                SelectedDisk = PhysicalDisks[0];
        }
        catch (Exception ex)
        {
            _log.Log($"Error loading physical disks: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadHealthDataAsync(CancellationToken ct)
    {
        if (SelectedDisk is null) return;

        IsBusy = true;
        try
        {
            _log.Log($"Loading health data for {SelectedDisk.FriendlyName}...");

            var smartData = await _wmiService.GetSmartDataAsync(SelectedDisk.DeviceId);
            ct.ThrowIfCancellationRequested();
            Smart = smartData;

            if (smartData is not null)
            {
                _log.Log($"SMART data loaded — Temperature: {smartData.Temperature?.ToString() ?? "N/A"}C, " +
                          $"Wear: {smartData.Wear?.ToString() ?? "N/A"}%, " +
                          $"Power-on hours: {smartData.PowerOnHours?.ToString() ?? "N/A"}");

                await _history.RecordAsync(SelectedDisk.DeviceId, smartData);
                var readings = await _history.GetHistoryAsync(SelectedDisk.DeviceId);
                var trends = SmartHistoryService.AnalyzeTrends(readings);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Trends.Clear();
                    foreach (var t in trends)
                        Trends.Add(t);
                });
                HistoryCount = readings.Count;
                OnPropertyChanged(nameof(HasTrends));
                OnPropertyChanged(nameof(HistoryCountText));

                if (trends.Count > 0)
                    _log.Log($"SMART trends: {trends.Count} alert(s) — {string.Join("; ", trends.Select(t => t.Message))}");
            }
            else
            {
                _log.Log("No SMART data available for this disk.");
                Application.Current.Dispatcher.Invoke(() => Trends.Clear());
                HistoryCount = 0;
                OnPropertyChanged(nameof(HasTrends));
                OnPropertyChanged(nameof(HistoryCountText));
            }

            _log.Log("Running partition alignment audit...");
            var alignments = await _wmiService.GetAlignmentAuditAsync();
            ct.ThrowIfCancellationRequested();

            Application.Current.Dispatcher.Invoke(() =>
            {
                AlignmentEntries.Clear();
                foreach (var a in alignments)
                    AlignmentEntries.Add(a);
            });

            int misaligned = alignments.Count(a => !a.IsAligned);
            if (misaligned > 0)
                _log.Log($"Alignment audit: {misaligned} misaligned partition(s) detected.");
            else
                _log.Log($"Alignment audit: all {alignments.Count} partition(s) are properly aligned.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.Log($"Error loading health data: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ToggleMonitoring()
    {
        if (IsMonitoring)
        {
            _tempMonitor.Stop();
            IsMonitoring = false;
            LiveTemperatureText = "";
        }
        else
        {
            _tempMonitor.Start(TimeSpan.FromSeconds(30));
            IsMonitoring = true;
        }
    }

    private void OnTemperatureAlert(object? sender, TemperatureAlert alert)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            TemperatureAlerts.Insert(0, alert);
            if (TemperatureAlerts.Count > 50)
                TemperatureAlerts.RemoveAt(TemperatureAlerts.Count - 1);
            OnPropertyChanged(nameof(HasTemperatureAlerts));
        });
    }

    private void OnTemperaturesUpdated(object? sender, Dictionary<string, int> temps)
    {
        if (temps.Count == 0) return;
        var parts = temps.Select(kv => $"Disk {kv.Key}: {kv.Value} C");
        var text = string.Join("  |  ", parts);
        Application.Current?.Dispatcher?.BeginInvoke(() => LiveTemperatureText = text);
    }
}
