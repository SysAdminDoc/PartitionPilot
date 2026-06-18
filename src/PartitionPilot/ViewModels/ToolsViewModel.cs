using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace PartitionPilot;

public class ToolsViewModel : ViewModelBase
{
    private readonly WmiDiskService _wmiService;
    private readonly ProcessRunner _processRunner;
    private readonly ActivityLog _log;
    private readonly IDialogService _dialog;
    private List<PhysicalDiskInfo> _physicalDisks = new();

    // ──────────────────────── MBR → GPT ────────────────────────

    public ObservableCollection<DiskInfo> MbrDisks { get; } = new();

    private DiskInfo? _selectedMbrDisk;
    public DiskInfo? SelectedMbrDisk
    {
        get => _selectedMbrDisk;
        set
        {
            if (SetProperty(ref _selectedMbrDisk, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    // ──────────────────────── FAT32 → NTFS ────────────────────────

    public ObservableCollection<char> Fat32Volumes { get; } = new();

    private char _selectedFat32Volume;
    public char SelectedFat32Volume
    {
        get => _selectedFat32Volume;
        set
        {
            if (SetProperty(ref _selectedFat32Volume, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    // ──────────────────────── Check / Optimize ────────────────────────

    public ObservableCollection<char> DriveLetters { get; } = new();

    public ObservableCollection<char> CheckVolumes => DriveLetters;

    private char _selectedCheckVolume;
    public char SelectedCheckVolume
    {
        get => _selectedCheckVolume;
        set
        {
            if (SetProperty(ref _selectedCheckVolume, value))
                SelectedCheckDrive = value;
        }
    }

    private char _selectedCheckDrive;
    public char SelectedCheckDrive
    {
        get => _selectedCheckDrive;
        set
        {
            if (SetProperty(ref _selectedCheckDrive, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private string _checkMode = "Scan";
    public string CheckMode
    {
        get => _checkMode;
        set
        {
            if (SetProperty(ref _checkMode, value))
            {
                OnPropertyChanged(nameof(CheckIsScan));
                OnPropertyChanged(nameof(CheckIsSpotFix));
                OnPropertyChanged(nameof(CheckIsOffline));
            }
        }
    }

    public bool CheckIsScan
    {
        get => CheckMode == "Scan";
        set { if (value) CheckMode = "Scan"; }
    }

    public bool CheckIsSpotFix
    {
        get => CheckMode == "SpotFix";
        set { if (value) CheckMode = "SpotFix"; }
    }

    public bool CheckIsOffline
    {
        get => CheckMode == "OfflineScanAndFix";
        set { if (value) CheckMode = "OfflineScanAndFix"; }
    }

    public ObservableCollection<char> OptVolumes => DriveLetters;

    private char _selectedOptVolume;
    public char SelectedOptVolume
    {
        get => _selectedOptVolume;
        set
        {
            if (SetProperty(ref _selectedOptVolume, value))
                SelectedOptDrive = value;
        }
    }

    private char _selectedOptDrive;
    public char SelectedOptDrive
    {
        get => _selectedOptDrive;
        set
        {
            if (SetProperty(ref _selectedOptDrive, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private string _optimizeMode = "Analyze";
    public string OptimizeMode
    {
        get => _optimizeMode;
        set
        {
            if (SetProperty(ref _optimizeMode, value))
            {
                OnPropertyChanged(nameof(OptIsAnalyze));
                OnPropertyChanged(nameof(OptIsDefrag));
                OnPropertyChanged(nameof(OptIsTrim));
            }
        }
    }

    public bool OptIsAnalyze
    {
        get => OptimizeMode == "Analyze";
        set { if (value) OptimizeMode = "Analyze"; }
    }

    public bool OptIsDefrag
    {
        get => OptimizeMode == "Defrag";
        set { if (value) OptimizeMode = "Defrag"; }
    }

    public bool OptIsTrim
    {
        get => OptimizeMode == "Retrim";
        set { if (value) OptimizeMode = "Retrim"; }
    }

    // ──────────────────────── Wipe ────────────────────────

    public ObservableCollection<DiskInfo> AllDisks { get; } = new();

    public ObservableCollection<VolumeInfo> WipeVolumes { get; } = new();

    public ObservableCollection<DiskInfo> WipeTargets => AllDisks;

    private VolumeInfo? _selectedWipeVolume;
    public VolumeInfo? SelectedWipeVolume
    {
        get => _selectedWipeVolume;
        set
        {
            if (SetProperty(ref _selectedWipeVolume, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private DiskInfo? _selectedWipeTarget;
    public DiskInfo? SelectedWipeTarget
    {
        get => _selectedWipeTarget;
        set
        {
            if (SetProperty(ref _selectedWipeTarget, value))
                SelectedWipeDrive = value;
        }
    }

    private DiskInfo? _selectedWipeDrive;
    public DiskInfo? SelectedWipeDrive
    {
        get => _selectedWipeDrive;
        set
        {
            if (SetProperty(ref _selectedWipeDrive, value))
            {
                OnPropertyChanged(nameof(IsNvmeSanitizeAvailable));
                OnPropertyChanged(nameof(NvmeSanitizeAvailabilityText));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private string _wipeMode = "SinglePass";
    public string WipeMode
    {
        get => _wipeMode;
        set
        {
            if (SetProperty(ref _wipeMode, value))
            {
                OnPropertyChanged(nameof(WipeIsFreeSpace));
                OnPropertyChanged(nameof(WipeIsFullDisk));
                OnPropertyChanged(nameof(WipeIsNvmeSanitize));
                OnPropertyChanged(nameof(IsNvmeSanitizeAvailable));
                OnPropertyChanged(nameof(NvmeSanitizeAvailabilityText));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool WipeIsFreeSpace
    {
        get => WipeMode == "FreeSpace";
        set { if (value) WipeMode = "FreeSpace"; }
    }

    public bool WipeIsFullDisk
    {
        get => WipeMode == "SinglePass";
        set { if (value) WipeMode = "SinglePass"; }
    }

    public bool WipeIsNvmeSanitize
    {
        get => WipeMode is "NvmeBlockErase" or "NvmeCryptoErase";
        set { if (value) WipeMode = "NvmeBlockErase"; }
    }

    public bool IsNvmeSanitizeAvailable =>
        SecureEraseService.CanSanitizeDisk(SelectedWipeDrive, _physicalDisks, out _);

    public string NvmeSanitizeAvailabilityText
    {
        get
        {
            SecureEraseService.CanSanitizeDisk(SelectedWipeDrive, _physicalDisks, out var reason);
            return reason;
        }
    }

    private bool _useCryptoErase;
    public bool UseCryptoErase
    {
        get => _useCryptoErase;
        set => SetProperty(ref _useCryptoErase, value);
    }

    // ──────────────────────── Boot Repair ────────────────────────

    public ObservableCollection<char> WindowsInstalls => DriveLetters;

    private char _selectedWindowsInstall;
    public char SelectedWindowsInstall
    {
        get => _selectedWindowsInstall;
        set
        {
            if (SetProperty(ref _selectedWindowsInstall, value))
                SelectedBootDrive = value;
        }
    }

    private char _selectedBootDrive;
    public char SelectedBootDrive
    {
        get => _selectedBootDrive;
        set
        {
            if (SetProperty(ref _selectedBootDrive, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    // ──────────────────────── Surface Test ────────────────────────

    public ObservableCollection<char> SurfaceTestVolumes => DriveLetters;

    private char _selectedSurfaceTestVolume;
    public char SelectedSurfaceTestVolume
    {
        get => _selectedSurfaceTestVolume;
        set
        {
            if (SetProperty(ref _selectedSurfaceTestVolume, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private string _surfaceTestResults = "";
    public string SurfaceTestResults
    {
        get => _surfaceTestResults;
        set => SetProperty(ref _surfaceTestResults, value);
    }

    // ──────────────────────── Dev Drive ────────────────────────

    private async Task CreateDevDriveAsync()
    {
        if (SelectedDevDriveLetter == default) return;

        if (!IsDevDriveSupported)
        {
            _dialog.ShowError("Dev Drive requires Windows 11 build 22621 or later.", "Dev Drive Not Supported");
            return;
        }

        if (!_dialog.ConfirmWarning(
            $"Format {SelectedDevDriveLetter}: as a Dev Drive (ReFS)?\n\n" +
            "ALL DATA ON THIS VOLUME WILL BE ERASED.\n\n" +
            "Dev Drive uses ReFS with optimized I/O performance and allows configuring antivirus filter exclusions. " +
            "Minimum 50 GB is recommended.",
            "Create Dev Drive")) return;

        if (!ConfirmBitLockerDestructiveVolume($"Create Dev Drive on {SelectedDevDriveLetter}:", SelectedDevDriveLetter))
            return;

        var ct = BeginOperation($"Creating Dev Drive on {SelectedDevDriveLetter}:...");
        try
        {
            _log.Log($"Formatting {SelectedDevDriveLetter}: as Dev Drive (ReFS)...");
            var cmd = $"Format-Volume -DriveLetter '{SelectedDevDriveLetter}' -DevDrive -FileSystem ReFS -Confirm:$false";
            var result = await _processRunner.RunPowerShellAsync(cmd, _log, ct);
            _log.Log($"Dev Drive result: {result.Trim()}");

            StatusText = "Designating as trusted Dev Drive...";
            await _processRunner.RunExeAsync("fsutil", $"devdrv trust {SelectedDevDriveLetter}:", _log, ct: ct);
            _log.Log($"Dev Drive {SelectedDevDriveLetter}: designated as trusted.");

            _dialog.ShowInfo($"Dev Drive created on {SelectedDevDriveLetter}: (ReFS).\n\nThe volume is designated as trusted.", "Dev Drive Created");
            await RefreshDriveListsAsync();
        }
        catch (OperationCanceledException)
        {
            _log.Log("Dev Drive creation cancelled.");
        }
        catch (Exception ex)
        {
            _log.Log($"Dev Drive creation failed: {ex.Message}");
            _dialog.ShowError($"Dev Drive creation failed:\n{ex.Message}", "Dev Drive Error");
        }
        finally
        {
            EndOperation();
        }
    }

    // ──────────────────────── Benchmark ────────────────────────

    public ObservableCollection<char> BenchDrives => DriveLetters;

    private char _selectedBenchDrive;
    public char SelectedBenchDrive
    {
        get => _selectedBenchDrive;
        set
        {
            if (SetProperty(ref _selectedBenchDrive, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private string _benchmarkResults = "";
    public string BenchmarkResults
    {
        get => _benchmarkResults;
        set => SetProperty(ref _benchmarkResults, value);
    }

    // ──────────────────────── Dev Drive ────────────────────────

    private char _selectedDevDriveLetter;
    public char SelectedDevDriveLetter
    {
        get => _selectedDevDriveLetter;
        set
        {
            if (SetProperty(ref _selectedDevDriveLetter, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsDevDriveSupported { get; } = Environment.OSVersion.Version.Build >= 22621;

    // ──────────────────────── Shared ────────────────────────

    private CancellationTokenSource? _cts;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    // ──────────────────────── Commands ────────────────────────

    public ICommand ValidateMbrToGptCommand { get; }
    public ICommand ConvertMbrToGptCommand { get; }
    public ICommand ConvertFat32Command { get; }
    public ICommand RunFsCheckCommand { get; }
    public ICommand RunOptimizeCommand { get; }
    public ICommand RunWipeCommand { get; }
    public ICommand RunBootRepairCommand { get; }
    public ICommand RunBenchmarkCommand { get; }
    public ICommand RunSurfaceTestCommand { get; }
    public ICommand CreateDevDriveCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CancelCommand { get; }

    public ToolsViewModel(WmiDiskService wmiService, ProcessRunner processRunner, ActivityLog log, IDialogService dialog)
    {
        _wmiService = wmiService;
        _processRunner = processRunner;
        _log = log;
        _dialog = dialog;

        ValidateMbrToGptCommand = new AsyncRelayCommand(_ => ValidateMbrToGptAsync(), _ => SelectedMbrDisk is not null);
        ConvertMbrToGptCommand = new AsyncRelayCommand(_ => ConvertMbrToGptAsync(), _ => SelectedMbrDisk is not null);
        ConvertFat32Command = new AsyncRelayCommand(_ => ConvertFat32Async(), _ => SelectedFat32Volume != default);
        RunFsCheckCommand = new AsyncRelayCommand(_ => RunFsCheckAsync(), _ => SelectedCheckVolume != default);
        RunOptimizeCommand = new AsyncRelayCommand(_ => RunOptimizeAsync(), _ => SelectedOptVolume != default);
        RunWipeCommand = new AsyncRelayCommand(
            _ => RunWipeAsync(),
            _ => CanRunWipe());
        RunBootRepairCommand = new AsyncRelayCommand(_ => RunBootRepairAsync(), _ => SelectedBootDrive != default);
        RunBenchmarkCommand = new AsyncRelayCommand(_ => RunBenchmarkAsync(SelectedBenchDrive), _ => SelectedBenchDrive != default);
        RunSurfaceTestCommand = new AsyncRelayCommand(_ => RunSurfaceTestAsync(), _ => SelectedSurfaceTestVolume != default);
        CreateDevDriveCommand = new AsyncRelayCommand(_ => CreateDevDriveAsync(), _ => IsDevDriveSupported && SelectedDevDriveLetter != default);
        RefreshCommand = new AsyncRelayCommand(_ => RefreshDriveListsAsync());
        CancelCommand = new RelayCommand(_ => CancelCurrentOperation(), _ => IsBusy && _cts is not null);
    }

    private bool CanRunWipe()
    {
        if (WipeIsFreeSpace)
            return SelectedWipeVolume?.DriveLetter is not null;

        if (SelectedWipeDrive is null)
            return false;

        return !WipeIsNvmeSanitize || IsNvmeSanitizeAvailable;
    }

    private CancellationToken BeginOperation(string status)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        StatusText = status;
        IsBusy = true;
        return _cts.Token;
    }

    private void EndOperation(string? status = null)
    {
        StatusText = status ?? "";
        IsBusy = false;
        _cts?.Dispose();
        _cts = null;
    }

    private void CancelCurrentOperation()
    {
        _cts?.Cancel();
        _log.Log("Operation cancelled by user.");
        StatusText = "Cancelling...";
    }

    // ──────────────────────── Refresh ────────────────────────

    public async Task RefreshDriveListsAsync()
    {
        IsBusy = true;
        try
        {
            _log.Log("Refreshing tools drive lists...");

            var disks = await _wmiService.GetDisksAsync();
            var volumes = await _wmiService.GetVolumesAsync();
            var physicalDisks = await _wmiService.GetPhysicalDisksAsync();
            var bitlockerStatus = await _wmiService.GetBitLockerStatusAsync();

            var mbrOnly = disks.Where(d => d.PartitionStyle.Equals("MBR", StringComparison.OrdinalIgnoreCase)).ToList();
            var letters = volumes
                .Where(v => v.DriveLetter.HasValue)
                .Select(v => v.DriveLetter!.Value)
                .OrderBy(c => c)
                .ToList();

            var fat32Letters = volumes
                .Where(v => v.DriveLetter.HasValue && v.FileSystemType.Equals("FAT32", StringComparison.OrdinalIgnoreCase))
                .Select(v => v.DriveLetter!.Value)
                .OrderBy(c => c)
                .ToList();

            Application.Current.Dispatcher.Invoke(() =>
            {
                _physicalDisks = physicalDisks;

                foreach (var volume in volumes.Where(v => v.DriveLetter.HasValue))
                {
                    if (bitlockerStatus.TryGetValue(volume.DriveLetter!.Value, out var status))
                        volume.EncryptionStatus = status;
                }

                MbrDisks.Clear();
                foreach (var d in mbrOnly)
                    MbrDisks.Add(d);

                AllDisks.Clear();
                foreach (var d in disks)
                    AllDisks.Add(d);

                WipeVolumes.Clear();
                foreach (var v in volumes.Where(v => v.DriveLetter.HasValue).OrderBy(v => v.DriveLetter))
                    WipeVolumes.Add(v);

                DriveLetters.Clear();
                foreach (var l in letters)
                    DriveLetters.Add(l);

                Fat32Volumes.Clear();
                foreach (var l in fat32Letters)
                    Fat32Volumes.Add(l);

                SelectedMbrDisk = PickDiskSelection(MbrDisks, SelectedMbrDisk, autoSelect: false);
                SelectedFat32Volume = PickDriveSelection(Fat32Volumes, SelectedFat32Volume, autoSelect: false);
                SelectedCheckVolume = PickDriveSelection(DriveLetters, SelectedCheckVolume, autoSelect: true);
                SelectedOptVolume = PickDriveSelection(DriveLetters, SelectedOptVolume, autoSelect: true);
                SelectedWindowsInstall = PickDriveSelection(DriveLetters, SelectedWindowsInstall, autoSelect: false);
                SelectedSurfaceTestVolume = PickDriveSelection(DriveLetters, SelectedSurfaceTestVolume, autoSelect: true);
                SelectedBenchDrive = PickDriveSelection(DriveLetters, SelectedBenchDrive, autoSelect: true);
                SelectedDevDriveLetter = PickDriveSelection(DriveLetters, SelectedDevDriveLetter, autoSelect: false);
                SelectedWipeVolume = PickVolumeSelection(WipeVolumes, SelectedWipeVolume, autoSelect: true);
                SelectedWipeDrive = PickDiskSelection(AllDisks, SelectedWipeDrive, autoSelect: true);
                SelectedWipeTarget = SelectedWipeDrive;
                OnPropertyChanged(nameof(IsNvmeSanitizeAvailable));
                OnPropertyChanged(nameof(NvmeSanitizeAvailabilityText));
            });

            _log.Log($"Tools refresh: {disks.Count} disk(s), {physicalDisks.Count} physical disk(s), {mbrOnly.Count} MBR, {letters.Count} volume(s).");
        }
        catch (Exception ex)
        {
            _log.Log($"Error refreshing drive lists: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ──────────────────────── MBR → GPT ────────────────────────

    public static char PickDriveSelection(IEnumerable<char> availableLetters, char current, bool autoSelect)
    {
        var letters = availableLetters
            .Where(char.IsLetter)
            .Select(char.ToUpperInvariant)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        if (current != default)
        {
            var normalizedCurrent = char.ToUpperInvariant(current);
            if (letters.Contains(normalizedCurrent))
                return normalizedCurrent;
        }

        return autoSelect ? letters.FirstOrDefault() : default;
    }

    public static DiskInfo? PickDiskSelection(IEnumerable<DiskInfo> disks, DiskInfo? current, bool autoSelect)
    {
        var list = disks.OrderBy(d => d.Number).ToList();
        if (current is not null)
        {
            var match = list.FirstOrDefault(d => d.Number == current.Number);
            if (match is not null)
                return match;
        }

        return autoSelect ? list.FirstOrDefault() : null;
    }

    public static VolumeInfo? PickVolumeSelection(IEnumerable<VolumeInfo> volumes, VolumeInfo? current, bool autoSelect)
    {
        var list = volumes
            .Where(v => v.DriveLetter.HasValue)
            .OrderBy(v => v.DriveLetter)
            .ToList();

        if (current?.DriveLetter is char currentLetter)
        {
            var normalizedCurrent = char.ToUpperInvariant(currentLetter);
            var match = list.FirstOrDefault(v =>
                v.DriveLetter.HasValue &&
                char.ToUpperInvariant(v.DriveLetter.Value) == normalizedCurrent);
            if (match is not null)
                return match;
        }

        return autoSelect ? list.FirstOrDefault() : null;
    }

    private async Task ValidateMbrToGptAsync()
    {
        if (SelectedMbrDisk is null) return;

        IsBusy = true;
        try
        {
            _log.Log($"Validating MBR to GPT conversion for Disk {SelectedMbrDisk.Number}...");
            var result = await _processRunner.RunExeAsync("mbr2gpt", $"/validate /disk:{SelectedMbrDisk.Number} /allowFullOS", _log);
            _log.Log($"Validation result:\n{result.Trim()}");

            _dialog.ShowInfo(result.Trim(), "MBR2GPT Validation");
        }
        catch (Exception ex)
        {
            _log.Log($"MBR2GPT validation failed: {ex.Message}");
            _dialog.ShowError($"Validation failed:\n{ex.Message}", "MBR2GPT Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConvertMbrToGptAsync()
    {
        if (SelectedMbrDisk is null) return;

        if (!_dialog.ConfirmWarning(
            $"Convert Disk {SelectedMbrDisk.Number} ({SelectedMbrDisk.FriendlyName}) from MBR to GPT?\n\n" +
            "This operation is irreversible. Ensure you have validated first.",
            "Confirm MBR to GPT Conversion")) return;

        IsBusy = true;
        try
        {
            _log.Log($"Converting Disk {SelectedMbrDisk.Number} from MBR to GPT...");
            var result = await _processRunner.RunExeAsync("mbr2gpt", $"/convert /disk:{SelectedMbrDisk.Number} /allowFullOS", _log);
            _log.Log($"Conversion result:\n{result.Trim()}");

            _dialog.ShowInfo("MBR to GPT conversion completed successfully.", "Conversion Complete");

            await RefreshDriveListsAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"MBR to GPT conversion failed: {ex.Message}");
            _dialog.ShowError($"Conversion failed:\n{ex.Message}", "MBR2GPT Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ──────────────────────── FAT32 → NTFS ────────────────────────

    private async Task ConvertFat32Async()
    {
        if (SelectedFat32Volume == default) return;

        if (!_dialog.ConfirmWarning(
            $"Convert {SelectedFat32Volume}: from FAT32 to NTFS?\n\nThis is a one-way conversion.",
            "Confirm FAT32 to NTFS")) return;

        IsBusy = true;
        try
        {
            _log.Log($"Converting {SelectedFat32Volume}: from FAT32 to NTFS...");
            var result = await _processRunner.RunExeAsync("convert.exe", $"{SelectedFat32Volume}: /FS:NTFS /NoSecurity /X", _log);
            _log.Log($"Convert result:\n{result.Trim()}");

            _dialog.ShowInfo($"Conversion complete for {SelectedFat32Volume}:.", "Conversion Complete");
        }
        catch (Exception ex)
        {
            _log.Log($"FAT32 conversion failed: {ex.Message}");
            _dialog.ShowError($"Conversion failed:\n{ex.Message}", "Convert Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ──────────────────────── File System Check ────────────────────────

    private async Task RunFsCheckAsync()
    {
        if (SelectedCheckDrive == default) return;

        var ct = BeginOperation($"Checking {SelectedCheckDrive}: ({CheckMode})...");
        try
        {
            string mode = CheckMode switch
            {
                "SpotFix" => "-SpotFix",
                "OfflineScanAndFix" => "-OfflineScanAndFix",
                _ => "-Scan"
            };

            _log.Log($"Running Repair-Volume on {SelectedCheckDrive}: ({CheckMode})...");
            var cmd = $"Repair-Volume -DriveLetter '{SelectedCheckDrive}' {mode}";
            var result = await _processRunner.RunPowerShellAsync(cmd, _log, ct);
            _log.Log($"Check result: {result.Trim()}");

            _dialog.ShowInfo($"File system check ({CheckMode}) on {SelectedCheckDrive}: completed.\n\n{result.Trim()}",
                "Check Complete");
        }
        catch (OperationCanceledException)
        {
            _log.Log("File system check cancelled.");
        }
        catch (Exception ex)
        {
            _log.Log($"File system check failed: {ex.Message}");
            _dialog.ShowError($"Check failed:\n{ex.Message}", "Check Error");
        }
        finally
        {
            EndOperation();
        }
    }

    // ──────────────────────── Optimize ────────────────────────

    private async Task RunOptimizeAsync()
    {
        if (SelectedOptDrive == default) return;

        var ct = BeginOperation($"Optimizing {SelectedOptDrive}: ({OptimizeMode})...");
        try
        {
            string mode = OptimizeMode switch
            {
                "Defrag" => "-Defrag",
                "Retrim" => "-Retrim",
                "SlabConsolidate" => "-SlabConsolidate",
                _ => "-Analyze"
            };

            _log.Log($"Running Optimize-Volume on {SelectedOptDrive}: ({OptimizeMode})...");
            var cmd = $"Optimize-Volume -DriveLetter '{SelectedOptDrive}' {mode} -Verbose";
            var result = await _processRunner.RunPowerShellAsync(cmd, _log, ct);
            _log.Log($"Optimize result: {result.Trim()}");

            _dialog.ShowInfo($"Optimization ({OptimizeMode}) on {SelectedOptDrive}: completed.\n\n{result.Trim()}",
                "Optimize Complete");
        }
        catch (OperationCanceledException)
        {
            _log.Log("Optimization cancelled.");
        }
        catch (Exception ex)
        {
            _log.Log($"Optimization failed: {ex.Message}");
            _dialog.ShowError($"Optimization failed:\n{ex.Message}", "Optimize Error");
        }
        finally
        {
            EndOperation();
        }
    }

    // ──────────────────────── Disk Wipe ────────────────────────

    private async Task RunFreeSpaceWipeAsync()
    {
        if (SelectedWipeVolume?.DriveLetter is not char letter) return;

        var encryptionStatus = SelectedWipeVolume.EncryptionStatus;
        if (BitLockerPreflight.RequiresUnlockForRead(encryptionStatus))
        {
            _dialog.ShowError(
                BitLockerPreflight.BuildUnlockRequiredMessage($"Wipe free space on {letter}:", $"{letter}:", encryptionStatus),
                "BitLocker Volume Locked");
            return;
        }

        if (!ConfirmBitLockerDestructiveVolume($"Wipe free space on {letter}:", letter, encryptionStatus))
            return;

        var encryptionLine = string.IsNullOrWhiteSpace(encryptionStatus) ? "" : $"\nEncryption: {encryptionStatus}\n";
        if (!_dialog.Confirm(
            $"Wipe free space on {letter}:?\n{encryptionLine}\nExisting files remain in place. Previously deleted data in free space will be overwritten.",
            "Confirm Free-Space Wipe")) return;

        var ct = BeginOperation($"Wiping free space on {letter}:...");
        try
        {
            _log.Log($"Wiping free space on {letter}: with cipher /w...");
            using var volumeLock = VolumeLockService.RequireLock(letter, _log);
            await _processRunner.RunExeAsync("cipher", $"/w:{letter}:\\", _log, ct: ct);

            _log.Log($"Free-space wipe complete on {letter}:.");
            _dialog.ShowInfo($"Free-space wipe complete on {letter}:.", "Wipe Complete");

            await RefreshDriveListsAsync();
        }
        catch (OperationCanceledException)
        {
            _log.Log($"Free-space wipe on {letter}: cancelled.");
        }
        catch (Exception ex)
        {
            _log.Log($"Free-space wipe failed: {ex.Message}");
            _dialog.ShowError($"Free-space wipe failed:\n{ex.Message}", "Wipe Error");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task RunNvmeSanitizeAsync()
    {
        if (SelectedWipeDrive is null) return;

        if (!SecureEraseService.CanSanitizeDisk(SelectedWipeDrive, _physicalDisks, out var availability))
        {
            _dialog.ShowError(availability, "NVMe Sanitize Not Available");
            return;
        }

        var method = UseCryptoErase
            ? SecureEraseService.SanitizeMethod.CryptoErase
            : SecureEraseService.SanitizeMethod.BlockErase;

        var protectedTargets = await GetBitLockerProtectedTargetsAsync(SelectedWipeDrive.Number);
        if (protectedTargets.Count > 0 &&
            !_dialog.ConfirmDanger(
                BitLockerPreflight.BuildDestructiveConfirmation(
                    $"NVMe sanitize Disk {SelectedWipeDrive.Number}",
                    protectedTargets),
                "Confirm BitLocker-Protected Sanitize"))
        {
            return;
        }

        if (!_dialog.ConfirmDanger(
            $"NVMe FIRMWARE ERASE on Disk {SelectedWipeDrive.Number} ({SelectedWipeDrive.FriendlyName}).\n\n" +
            $"Method: {method}\n\n" +
            "This sends a firmware-level sanitize command directly to the drive controller. " +
            "ALL DATA WILL BE PERMANENTLY AND IRREVERSIBLY DESTROYED.\n\n" +
            "This operation cannot be cancelled once started.",
            "NVMe Sanitize -- Confirmation 1 of 2")) return;

        if (!_dialog.ConfirmDanger(
            "FINAL WARNING: NVMe sanitize is a hardware-level operation that erases ALL data " +
            "including data in over-provisioned and remapped sectors.\n\nProceed?",
            "NVMe Sanitize -- FINAL Confirmation")) return;

        var ct = BeginOperation($"NVMe sanitize ({method}) on Disk {SelectedWipeDrive.Number}...");
        try
        {
            int diskNum = SelectedWipeDrive.Number;
            _log.Log($"Starting NVMe sanitize ({method}) on Disk {diskNum}...");

            await Task.Run(() => SecureEraseService.ExecuteNvmeSanitize(diskNum, method, _log), ct);

            _log.Log($"NVMe sanitize ({method}) completed on Disk {diskNum}.");
            _dialog.ShowInfo($"NVMe sanitize ({method}) completed on Disk {diskNum}.", "Sanitize Complete");
            await RefreshDriveListsAsync();
        }
        catch (OperationCanceledException)
        {
            _log.Log("NVMe sanitize cancelled.");
        }
        catch (Exception ex)
        {
            _log.Log($"NVMe sanitize failed: {ex.Message}");
            _dialog.ShowError($"NVMe sanitize failed:\n{ex.Message}\n\nThe drive may not support this sanitize method.", "Sanitize Error");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task RunWipeAsync()
    {
        if (WipeIsFreeSpace)
        {
            await RunFreeSpaceWipeAsync();
            return;
        }

        if (WipeIsNvmeSanitize)
        {
            await RunNvmeSanitizeAsync();
            return;
        }

        if (SelectedWipeDrive is null) return;

        var protectedTargets = await GetBitLockerProtectedTargetsAsync(SelectedWipeDrive.Number);
        if (protectedTargets.Count > 0 &&
            !_dialog.ConfirmDanger(
                BitLockerPreflight.BuildDestructiveConfirmation(
                    $"Wipe Disk {SelectedWipeDrive.Number}",
                    protectedTargets),
                "Confirm BitLocker-Protected Wipe"))
        {
            return;
        }

        if (!_dialog.ConfirmWarning(
            $"WARNING: You are about to wipe Disk {SelectedWipeDrive.Number} ({SelectedWipeDrive.FriendlyName}).\n\n" +
            "ALL DATA ON THIS DISK WILL BE PERMANENTLY DESTROYED.\n\nContinue?",
            "Wipe Disk -- Confirmation 1 of 3")) return;

        if (!_dialog.ConfirmWarning(
            $"Are you absolutely sure you want to wipe Disk {SelectedWipeDrive.Number}?\n\n" +
            $"Disk: {SelectedWipeDrive.FriendlyName}\n" +
            $"Size: {SizeUtil.Format(SelectedWipeDrive.Size)}\n" +
            $"Mode: {WipeMode}\n\nThis CANNOT be undone.",
            "Wipe Disk -- Confirmation 2 of 3")) return;

        if (!_dialog.ConfirmDanger(
            "FINAL WARNING: Click Yes to begin disk wipe immediately.",
            "Wipe Disk -- FINAL Confirmation")) return;

        var ct = BeginOperation($"Wiping Disk {SelectedWipeDrive.Number}...");
        var locks = new List<VolumeLock>();
        try
        {
            int diskNum = SelectedWipeDrive.Number;
            _log.Log($"Wiping Disk {diskNum} ({WipeMode})...");

            // Best-effort lock on all known volumes of this disk
            var volumes = await _wmiService.GetVolumesAsync();
            var partitions = await _wmiService.GetPartitionsAsync(diskNum);
            var diskLetters = partitions
                .Where(p => p.DriveLetter.HasValue)
                .Select(p => p.DriveLetter!.Value)
                .ToList();
            locks = diskLetters
                .Select(l => VolumeLockService.RequireLock(l, _log))
                .ToList();

            StatusText = $"Clearing Disk {diskNum}...";
            var clearCmd = $"Clear-Disk -Number {diskNum} -RemoveData -RemoveOEM -Confirm:$false";
            await _processRunner.RunPowerShellAsync(clearCmd, _log, ct);
            _log.Log($"Disk {diskNum} cleared.");

            if (WipeMode != "SinglePass")
            {
                StatusText = "Running multi-pass wipe with cipher /w...";
                _log.Log("Running multi-pass wipe with cipher /w...");

                var initCmd = $"Initialize-Disk -Number {diskNum} -PartitionStyle GPT -Confirm:$false";
                await _processRunner.RunPowerShellAsync(initCmd, _log, ct);

                var createPartCmd = $"New-Partition -DiskNumber {diskNum} -UseMaximumSize -AssignDriveLetter | Format-Volume -FileSystem NTFS -Confirm:$false";
                await _processRunner.RunPowerShellAsync(createPartCmd, _log, ct);

                var letterCmd = $"(Get-Partition -DiskNumber {diskNum} | Where-Object {{ $_.DriveLetter }} | Select-Object -First 1).DriveLetter";
                var letterResult = await _processRunner.RunPowerShellAsync(letterCmd, _log, ct);
                var letter = letterResult.Trim();

                if (!string.IsNullOrEmpty(letter))
                {
                    StatusText = $"Cipher wiping {letter}:\\...";
                    await _processRunner.RunExeAsync("cipher", $"/w:{letter}:\\", _log, ct: ct);
                    _log.Log($"Cipher wipe complete on {letter}:\\.");
                }

                StatusText = "Final disk clear...";
                await _processRunner.RunPowerShellAsync(clearCmd, _log, ct);
            }

            _log.Log($"Disk {diskNum} wipe complete.");
            _dialog.ShowInfo($"Disk {diskNum} has been wiped.", "Wipe Complete");

            await RefreshDriveListsAsync();
        }
        catch (OperationCanceledException)
        {
            _log.Log("Disk wipe cancelled.");
        }
        catch (Exception ex)
        {
            _log.Log($"Wipe failed: {ex.Message}");
            _dialog.ShowError($"Wipe failed:\n{ex.Message}", "Wipe Error");
        }
        finally
        {
            foreach (var l in locks) l?.Dispose();
            EndOperation();
        }
    }

    private bool ConfirmBitLockerDestructiveVolume(string operation, char letter)
    {
        return ConfirmBitLockerDestructiveVolume(operation, letter, GetVolumeEncryptionStatus(letter));
    }

    private bool ConfirmBitLockerDestructiveVolume(string operation, char letter, string? encryptionStatus)
    {
        if (!BitLockerPreflight.IsProtected(encryptionStatus))
            return true;

        return _dialog.ConfirmDanger(
            BitLockerPreflight.BuildDestructiveConfirmation(
                operation,
                new[] { $"{char.ToUpperInvariant(letter)}: {BitLockerPreflight.Describe(encryptionStatus)}" }),
            "Confirm BitLocker-Protected Data Loss");
    }

    private string? GetVolumeEncryptionStatus(char letter)
    {
        letter = char.ToUpperInvariant(letter);
        return WipeVolumes.FirstOrDefault(v =>
            v.DriveLetter.HasValue &&
            char.ToUpperInvariant(v.DriveLetter.Value) == letter)?.EncryptionStatus;
    }

    private async Task<List<string>> GetBitLockerProtectedTargetsAsync(int diskNumber)
    {
        var partitions = await _wmiService.GetPartitionsAsync(diskNumber);
        var bitlockerStatus = await _wmiService.GetBitLockerStatusAsync();
        foreach (var partition in partitions.Where(p => p.DriveLetter.HasValue))
        {
            if (bitlockerStatus.TryGetValue(partition.DriveLetter!.Value, out var status))
                partition.EncryptionStatus = status;
        }

        return partitions
            .Where(p => BitLockerPreflight.IsProtected(p.EncryptionStatus))
            .Select(BitLockerPreflight.DescribePartitionTarget)
            .ToList();
    }

    // ──────────────────────── Boot Repair ────────────────────────

    private async Task RunBootRepairAsync()
    {
        if (SelectedBootDrive == default) return;

        IsBusy = true;
        try
        {
            await using var cleanup = new OperationCleanupScope(_log);
            _log.Log($"Running boot repair for {SelectedBootDrive}:...");

            // Detect firmware type
            var fwCmd = "$env:firmware_type";
            var firmware = (await _processRunner.RunPowerShellAsync(fwCmd, _log)).Trim();
            _log.Log($"Firmware type: {firmware}");

            string bcdbootArgs;
            if (firmware.Equals("UEFI", StringComparison.OrdinalIgnoreCase))
            {
                _log.Log("Detected UEFI firmware — locating EFI System Partition...");
                var efiLetterCmd = "(Get-Partition | Where-Object { $_.GptType -eq '{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}' } | Select-Object -First 1).DriveLetter";
                var efiLetter = (await _processRunner.RunPowerShellAsync(efiLetterCmd, _log)).Trim();

                if (string.IsNullOrEmpty(efiLetter) || efiLetter.Length != 1 || !char.IsLetter(efiLetter[0]))
                {
                    // EFI partition has no letter assigned — temporarily assign one
                    _log.Log("EFI partition has no drive letter, assigning a temporary letter...");
                    var assignCmd = "$esp = Get-Partition | Where-Object { $_.GptType -eq '{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}' } | Select-Object -First 1; " +
                                    "Add-PartitionAccessPath -DiskNumber $esp.DiskNumber -PartitionNumber $esp.PartitionNumber -AssignDriveLetter; " +
                                    "(Get-Partition -DiskNumber $esp.DiskNumber -PartitionNumber $esp.PartitionNumber).DriveLetter";
                    efiLetter = (await _processRunner.RunPowerShellAsync(assignCmd, _log)).Trim();
                    if (!string.IsNullOrEmpty(efiLetter) && char.IsLetter(efiLetter[0]))
                    {
                        var temporaryLetter = char.ToUpperInvariant(efiLetter[0]);
                        var temporaryAccessPath = $"{temporaryLetter}:\\";
                        var removeAccessPathCmd =
                            $"Get-Partition -DriveLetter '{temporaryLetter}' | Remove-PartitionAccessPath -AccessPath {ProcessRunner.EscapePowerShellString(temporaryAccessPath)}";
                        cleanup.Register(
                            $"Remove temporary EFI access path {temporaryAccessPath}",
                            () => _processRunner.RunPowerShellAsync(removeAccessPathCmd, _log),
                            $"Run Remove-PartitionAccessPath for {temporaryAccessPath} from an elevated PowerShell session.");
                    }
                }

                if (string.IsNullOrEmpty(efiLetter) || !char.IsLetter(efiLetter[0]))
                {
                    throw new InvalidOperationException("Could not locate or mount the EFI System Partition.");
                }

                _log.Log($"EFI System Partition at {efiLetter}:");
                bcdbootArgs = $"{SelectedBootDrive}:\\Windows /s {efiLetter}: /f UEFI";
            }
            else
            {
                bcdbootArgs = $"{SelectedBootDrive}:\\Windows /s {SelectedBootDrive}: /f BIOS";
                _log.Log("Detected BIOS firmware — using legacy boot repair.");
            }

            var result = await _processRunner.RunExeAsync("bcdboot", bcdbootArgs, _log);
            _log.Log($"Boot repair result: {result.Trim()}");

            _dialog.ShowInfo($"Boot repair completed.\n\n{result.Trim()}", "Boot Repair Complete");
        }
        catch (Exception ex)
        {
            _log.Log($"Boot repair failed: {ex.Message}");
            _dialog.ShowError($"Boot repair failed:\n{ex.Message}", "Boot Repair Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ──────────────────────── Surface Test ────────────────────────

    private async Task RunSurfaceTestAsync()
    {
        if (SelectedSurfaceTestVolume == default) return;

        var ct = BeginOperation($"Running surface test on {SelectedSurfaceTestVolume}:...");
        SurfaceTestResults = "Running surface test (this may take a while)...";
        try
        {
            _log.Log($"Starting surface test (chkdsk /R) on {SelectedSurfaceTestVolume}:...");
            var cmd = $"Repair-Volume -DriveLetter '{SelectedSurfaceTestVolume}' -OfflineScanAndFix";
            var result = await _processRunner.RunPowerShellAsync(cmd, _log, ct);
            SurfaceTestResults = result.Trim();
            _log.Log($"Surface test complete: {result.Trim()}");
            _dialog.ShowInfo($"Surface test on {SelectedSurfaceTestVolume}: completed.\n\n{result.Trim()}", "Surface Test Complete");
        }
        catch (OperationCanceledException)
        {
            _log.Log("Surface test cancelled.");
            SurfaceTestResults = "Surface test cancelled.";
        }
        catch (Exception ex)
        {
            _log.Log($"Surface test failed: {ex.Message}");
            SurfaceTestResults = $"Failed: {ex.Message}";
            _dialog.ShowError($"Surface test failed:\n{ex.Message}", "Surface Test Error");
        }
        finally
        {
            EndOperation();
        }
    }

    // ──────────────────────── Benchmark ────────────────────────

    private async Task RunBenchmarkAsync(char driveLetter)
    {
        if (driveLetter == default) return;

        var ct = BeginOperation($"Benchmarking {driveLetter}:...");
        BenchmarkResults = "Running benchmark...";
        try
        {
            _log.Log($"Starting disk benchmark on {driveLetter}:...");

            var progress = new Progress<string>(msg =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    BenchmarkResults = msg;
                    StatusText = msg.Split('\n')[0];
                });
            });

            await Task.Run(() => RunBenchmarkCore(driveLetter, progress, ct), ct);

            _log.Log($"Benchmark complete for {driveLetter}:.");
        }
        catch (OperationCanceledException)
        {
            _log.Log("Benchmark cancelled.");
            BenchmarkResults = "Benchmark cancelled.";
        }
        catch (Exception ex)
        {
            _log.Log($"Benchmark failed: {ex.Message}");
            BenchmarkResults = $"Benchmark failed: {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    private void RunBenchmarkCore(char driveLetter, IProgress<string> progress, CancellationToken ct)
    {
        const int fileSizeMB = 256;
        const int blockSize = 1024 * 1024; // 1 MB
        const int totalBlocks = fileSizeMB;
        const int random4KOps = 500;
        const int random4KBlockSize = 4096;

        string tempPath = Path.Combine($"{driveLetter}:\\", $"pp_bench_{Guid.NewGuid():N}.tmp");
        var cleanup = new OperationCleanupScope(_log);
        cleanup.RegisterFileDelete(tempPath);

        var sw = new Stopwatch();
        var results = new System.Text.StringBuilder();
        var rng = new Random();

        try
        {
            byte[] buffer = new byte[blockSize];
            rng.NextBytes(buffer);

            // ---- Sequential Write ----
            progress.Report("Sequential Write (1 MB blocks)...");
            sw.Restart();
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, blockSize, FileOptions.WriteThrough))
            {
                for (int i = 0; i < totalBlocks; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    fs.Write(buffer, 0, blockSize);
                }
            }
            sw.Stop();
            double seqWriteMBs = fileSizeMB / sw.Elapsed.TotalSeconds;
            results.AppendLine($"Sequential Write:  {seqWriteMBs:F1} MB/s  ({sw.Elapsed.TotalSeconds:F2}s)");
            progress.Report(results.ToString());

            // ---- Sequential Read ----
            progress.Report(results + "\nSequential Read (1 MB blocks)...");
            sw.Restart();
            using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None, blockSize, FileOptions.SequentialScan))
            {
                while (fs.Read(buffer, 0, blockSize) > 0)
                    ct.ThrowIfCancellationRequested();
            }
            sw.Stop();
            double seqReadMBs = fileSizeMB / sw.Elapsed.TotalSeconds;
            results.AppendLine($"Sequential Read:   {seqReadMBs:F1} MB/s  ({sw.Elapsed.TotalSeconds:F2}s)");
            progress.Report(results.ToString());

            // ---- Random 4K Write ----
            byte[] smallBuf = new byte[random4KBlockSize];
            rng.NextBytes(smallBuf);
            long fileSizeBytes = (long)fileSizeMB * 1024 * 1024;
            long maxOffset = fileSizeBytes - random4KBlockSize;

            progress.Report(results + "\nRandom 4K Write...");
            sw.Restart();
            using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Write, FileShare.None, random4KBlockSize, FileOptions.WriteThrough))
            {
                for (int i = 0; i < random4KOps; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    long offset = (long)(rng.NextDouble() * maxOffset);
                    offset = offset - (offset % random4KBlockSize);
                    fs.Seek(offset, SeekOrigin.Begin);
                    fs.Write(smallBuf, 0, random4KBlockSize);
                }
            }
            sw.Stop();
            double rand4KWriteIOPS = random4KOps / sw.Elapsed.TotalSeconds;
            double rand4KWriteMBs = (random4KOps * random4KBlockSize / (1024.0 * 1024.0)) / sw.Elapsed.TotalSeconds;
            results.AppendLine($"Random 4K Write:   {rand4KWriteIOPS:F0} IOPS  ({rand4KWriteMBs:F1} MB/s)");
            progress.Report(results.ToString());

            // ---- Random 4K Read ----
            progress.Report(results + "\nRandom 4K Read...");
            sw.Restart();
            using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None, random4KBlockSize, FileOptions.RandomAccess))
            {
                for (int i = 0; i < random4KOps; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    long offset = (long)(rng.NextDouble() * maxOffset);
                    offset = offset - (offset % random4KBlockSize);
                    fs.Seek(offset, SeekOrigin.Begin);
                    fs.ReadExactly(smallBuf, 0, random4KBlockSize);
                }
            }
            sw.Stop();
            double rand4KReadIOPS = random4KOps / sw.Elapsed.TotalSeconds;
            double rand4KReadMBs = (random4KOps * random4KBlockSize / (1024.0 * 1024.0)) / sw.Elapsed.TotalSeconds;
            results.AppendLine($"Random 4K Read:    {rand4KReadIOPS:F0} IOPS  ({rand4KReadMBs:F1} MB/s)");

            results.AppendLine();
            results.AppendLine("Benchmark complete.");
            progress.Report(results.ToString());

            _log.Log($"Benchmark {driveLetter}: SeqW={seqWriteMBs:F0} MB/s, SeqR={seqReadMBs:F0} MB/s, " +
                     $"Rnd4KW={rand4KWriteIOPS:F0} IOPS, Rnd4KR={rand4KReadIOPS:F0} IOPS");
        }
        finally
        {
            cleanup.Dispose();
        }
    }
}
