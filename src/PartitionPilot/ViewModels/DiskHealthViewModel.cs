using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace PartitionPilot;

public class DiskHealthViewModel : ViewModelBase
{
    private readonly WmiDiskService _wmiService;
    private readonly ActivityLog _log;

    public ObservableCollection<PhysicalDiskInfo> PhysicalDisks { get; } = new();
    public ObservableCollection<AlignmentInfo> AlignmentEntries { get; } = new();

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
                _ = LoadHealthDataAsync();
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
            }
        }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    // ──────────────────────── Display Properties ────────────────────────

    public bool HasSmartData => Smart is not null;

    public string DiskSizeText => SelectedDisk is not null ? SizeUtil.Format(SelectedDisk.Size) : "";

    public string SectorSizeText => SelectedDisk is not null
        ? $"Logical: {SelectedDisk.LogicalSectorSize} / Physical: {SelectedDisk.PhysicalSectorSize}"
        : "";

    public string TemperatureText => Smart?.Temperature is not null ? $"{Smart.Temperature} C" : "N/A";

    public string WearText => Smart?.Wear is not null ? $"{Smart.Wear}%" : "N/A";

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

    public ICommand RefreshCommand { get; }

    public DiskHealthViewModel(WmiDiskService wmiService, ActivityLog log)
    {
        _wmiService = wmiService;
        _log = log;

        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
    }

    public async Task RefreshAsync()
    {
        await LoadPhysicalDisksAsync();
        await LoadHealthDataAsync();
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

    private async Task LoadHealthDataAsync()
    {
        if (SelectedDisk is null) return;

        IsBusy = true;
        try
        {
            _log.Log($"Loading health data for {SelectedDisk.FriendlyName}...");

            // Load SMART data
            var smartData = await _wmiService.GetSmartDataAsync(SelectedDisk.DeviceId);
            Smart = smartData;

            if (smartData is not null)
            {
                _log.Log($"SMART data loaded — Temperature: {smartData.Temperature?.ToString() ?? "N/A"}C, " +
                          $"Wear: {smartData.Wear?.ToString() ?? "N/A"}%, " +
                          $"Power-on hours: {smartData.PowerOnHours?.ToString() ?? "N/A"}");
            }
            else
            {
                _log.Log("No SMART data available for this disk.");
            }

            // Load alignment audit
            _log.Log("Running partition alignment audit...");
            var alignments = await _wmiService.GetAlignmentAuditAsync();

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
        catch (Exception ex)
        {
            _log.Log($"Error loading health data: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
