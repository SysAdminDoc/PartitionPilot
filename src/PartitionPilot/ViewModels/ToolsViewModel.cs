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
                CommandManager.InvalidateRequerySuggested();
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
        get => WipeMode != "FreeSpace";
        set { if (value) WipeMode = "SinglePass"; }
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

    // ──────────────────────── Shared ────────────────────────

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

    // ──────────────────────── Commands ────────────────────────

    public ICommand ValidateMbrToGptCommand { get; }
    public ICommand ConvertMbrToGptCommand { get; }
    public ICommand ConvertFat32Command { get; }
    public ICommand RunFsCheckCommand { get; }
    public ICommand RunOptimizeCommand { get; }
    public ICommand RunWipeCommand { get; }
    public ICommand RunBootRepairCommand { get; }
    public ICommand RunBenchmarkCommand { get; }
    public ICommand RefreshCommand { get; }

    public ToolsViewModel(WmiDiskService wmiService, ProcessRunner processRunner, ActivityLog log)
    {
        _wmiService = wmiService;
        _processRunner = processRunner;
        _log = log;

        ValidateMbrToGptCommand = new AsyncRelayCommand(_ => ValidateMbrToGptAsync(), _ => SelectedMbrDisk is not null);
        ConvertMbrToGptCommand = new AsyncRelayCommand(_ => ConvertMbrToGptAsync(), _ => SelectedMbrDisk is not null);
        ConvertFat32Command = new AsyncRelayCommand(_ => ConvertFat32Async(), _ => SelectedFat32Volume != default);
        RunFsCheckCommand = new AsyncRelayCommand(_ => RunFsCheckAsync(), _ => SelectedCheckVolume != default);
        RunOptimizeCommand = new AsyncRelayCommand(_ => RunOptimizeAsync(), _ => SelectedOptVolume != default);
        RunWipeCommand = new AsyncRelayCommand(
            _ => RunWipeAsync(),
            _ => WipeIsFreeSpace ? SelectedWipeVolume?.DriveLetter is not null : SelectedWipeDrive is not null);
        RunBootRepairCommand = new AsyncRelayCommand(_ => RunBootRepairAsync(), _ => SelectedBootDrive != default);
        RunBenchmarkCommand = new AsyncRelayCommand(_ => RunBenchmarkAsync(SelectedBenchDrive), _ => SelectedBenchDrive != default);
        RefreshCommand = new AsyncRelayCommand(_ => RefreshDriveListsAsync());
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

                SelectedWipeVolume ??= WipeVolumes.FirstOrDefault();
                SelectedWipeDrive ??= AllDisks.FirstOrDefault();
            });

            _log.Log($"Tools refresh: {disks.Count} disk(s), {mbrOnly.Count} MBR, {letters.Count} volume(s).");
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

    private async Task ValidateMbrToGptAsync()
    {
        if (SelectedMbrDisk is null) return;

        IsBusy = true;
        try
        {
            _log.Log($"Validating MBR to GPT conversion for Disk {SelectedMbrDisk.Number}...");
            var result = await _processRunner.RunExeAsync("mbr2gpt", $"/validate /disk:{SelectedMbrDisk.Number} /allowFullOS", _log);
            _log.Log($"Validation result:\n{result.Trim()}");

            MessageBox.Show(result.Trim(), "MBR2GPT Validation", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _log.Log($"MBR2GPT validation failed: {ex.Message}");
            MessageBox.Show($"Validation failed:\n{ex.Message}", "MBR2GPT Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConvertMbrToGptAsync()
    {
        if (SelectedMbrDisk is null) return;

        var confirm = MessageBox.Show(
            $"Convert Disk {SelectedMbrDisk.Number} ({SelectedMbrDisk.FriendlyName}) from MBR to GPT?\n\n" +
            "This operation is irreversible. Ensure you have validated first.",
            "Confirm MBR to GPT Conversion",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            _log.Log($"Converting Disk {SelectedMbrDisk.Number} from MBR to GPT...");
            var result = await _processRunner.RunExeAsync("mbr2gpt", $"/convert /disk:{SelectedMbrDisk.Number} /allowFullOS", _log);
            _log.Log($"Conversion result:\n{result.Trim()}");

            MessageBox.Show("MBR to GPT conversion completed successfully.", "Conversion Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);

            await RefreshDriveListsAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"MBR to GPT conversion failed: {ex.Message}");
            MessageBox.Show($"Conversion failed:\n{ex.Message}", "MBR2GPT Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
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

        var confirm = MessageBox.Show(
            $"Convert {SelectedFat32Volume}: from FAT32 to NTFS?\n\nThis is a one-way conversion.",
            "Confirm FAT32 to NTFS",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            _log.Log($"Converting {SelectedFat32Volume}: from FAT32 to NTFS...");
            var result = await _processRunner.RunExeAsync("convert.exe", $"{SelectedFat32Volume}: /FS:NTFS /NoSecurity /X", _log);
            _log.Log($"Convert result:\n{result.Trim()}");

            MessageBox.Show($"Conversion complete for {SelectedFat32Volume}:.", "Conversion Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _log.Log($"FAT32 conversion failed: {ex.Message}");
            MessageBox.Show($"Conversion failed:\n{ex.Message}", "Convert Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
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

        IsBusy = true;
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
            var result = await _processRunner.RunPowerShellAsync(cmd, _log);
            _log.Log($"Check result: {result.Trim()}");

            MessageBox.Show($"File system check ({CheckMode}) on {SelectedCheckDrive}: completed.\n\n{result.Trim()}",
                "Check Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _log.Log($"File system check failed: {ex.Message}");
            MessageBox.Show($"Check failed:\n{ex.Message}", "Check Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ──────────────────────── Optimize ────────────────────────

    private async Task RunOptimizeAsync()
    {
        if (SelectedOptDrive == default) return;

        IsBusy = true;
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
            var result = await _processRunner.RunPowerShellAsync(cmd, _log);
            _log.Log($"Optimize result: {result.Trim()}");

            MessageBox.Show($"Optimization ({OptimizeMode}) on {SelectedOptDrive}: completed.\n\n{result.Trim()}",
                "Optimize Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _log.Log($"Optimization failed: {ex.Message}");
            MessageBox.Show($"Optimization failed:\n{ex.Message}", "Optimize Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ──────────────────────── Disk Wipe ────────────────────────

    private async Task RunFreeSpaceWipeAsync()
    {
        if (SelectedWipeVolume?.DriveLetter is not char letter) return;

        var confirm = MessageBox.Show(
            $"Wipe free space on {letter}:?\n\nExisting files remain in place. Previously deleted data in free space will be overwritten.",
            "Confirm Free-Space Wipe",
            MessageBoxButton.YesNo, MessageBoxImage.Information);

        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            _log.Log($"Wiping free space on {letter}: with cipher /w...");
            await _processRunner.RunExeAsync("cipher", $"/w:{letter}:\\", _log);

            _log.Log($"Free-space wipe complete on {letter}:.");
            MessageBox.Show($"Free-space wipe complete on {letter}:.", "Wipe Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);

            await RefreshDriveListsAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"Free-space wipe failed: {ex.Message}");
            MessageBox.Show($"Free-space wipe failed:\n{ex.Message}", "Wipe Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunWipeAsync()
    {
        if (WipeIsFreeSpace)
        {
            await RunFreeSpaceWipeAsync();
            return;
        }

        if (SelectedWipeDrive is null) return;

        // Triple confirmation for destructive wipe
        var confirm1 = MessageBox.Show(
            $"WARNING: You are about to wipe Disk {SelectedWipeDrive.Number} ({SelectedWipeDrive.FriendlyName}).\n\n" +
            "ALL DATA ON THIS DISK WILL BE PERMANENTLY DESTROYED.\n\nContinue?",
            "Wipe Disk — Confirmation 1 of 3",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm1 != MessageBoxResult.Yes) return;

        var confirm2 = MessageBox.Show(
            $"Are you absolutely sure you want to wipe Disk {SelectedWipeDrive.Number}?\n\n" +
            $"Disk: {SelectedWipeDrive.FriendlyName}\n" +
            $"Size: {SizeUtil.Format(SelectedWipeDrive.Size)}\n" +
            $"Mode: {WipeMode}\n\nThis CANNOT be undone.",
            "Wipe Disk — Confirmation 2 of 3",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm2 != MessageBoxResult.Yes) return;

        var confirm3 = MessageBox.Show(
            "FINAL WARNING: Click Yes to begin disk wipe immediately.",
            "Wipe Disk — FINAL Confirmation",
            MessageBoxButton.YesNo, MessageBoxImage.Stop);
        if (confirm3 != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            int diskNum = SelectedWipeDrive.Number;
            _log.Log($"Wiping Disk {diskNum} ({WipeMode})...");

            // Clear the disk first
            var clearCmd = $"Clear-Disk -Number {diskNum} -RemoveData -RemoveOEM -Confirm:$false";
            await _processRunner.RunPowerShellAsync(clearCmd, _log);
            _log.Log($"Disk {diskNum} cleared.");

            if (WipeMode != "SinglePass")
            {
                // For multi-pass wipe, use cipher on each volume or write zeros
                _log.Log("Running multi-pass wipe with cipher /w...");

                // Initialize the disk first so cipher has a volume to work with
                var initCmd = $"Initialize-Disk -Number {diskNum} -PartitionStyle GPT -Confirm:$false";
                await _processRunner.RunPowerShellAsync(initCmd, _log);

                var createPartCmd = $"New-Partition -DiskNumber {diskNum} -UseMaximumSize -AssignDriveLetter | Format-Volume -FileSystem NTFS -Confirm:$false";
                var partResult = await _processRunner.RunPowerShellAsync(createPartCmd, _log);

                // Extract the assigned letter and run cipher
                var letterCmd = $"(Get-Partition -DiskNumber {diskNum} | Where-Object {{ $_.DriveLetter }} | Select-Object -First 1).DriveLetter";
                var letterResult = await _processRunner.RunPowerShellAsync(letterCmd, _log);
                var letter = letterResult.Trim();

                if (!string.IsNullOrEmpty(letter))
                {
                    await _processRunner.RunExeAsync("cipher", $"/w:{letter}:\\", _log);
                    _log.Log($"Cipher wipe complete on {letter}:\\.");
                }

                // Clean up — clear again
                await _processRunner.RunPowerShellAsync(clearCmd, _log);
            }

            _log.Log($"Disk {diskNum} wipe complete.");
            MessageBox.Show($"Disk {diskNum} has been wiped.", "Wipe Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);

            await RefreshDriveListsAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"Wipe failed: {ex.Message}");
            MessageBox.Show($"Wipe failed:\n{ex.Message}", "Wipe Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ──────────────────────── Boot Repair ────────────────────────

    private async Task RunBootRepairAsync()
    {
        if (SelectedBootDrive == default) return;

        IsBusy = true;
        try
        {
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

            MessageBox.Show($"Boot repair completed.\n\n{result.Trim()}", "Boot Repair Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _log.Log($"Boot repair failed: {ex.Message}");
            MessageBox.Show($"Boot repair failed:\n{ex.Message}", "Boot Repair Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ──────────────────────── Benchmark ────────────────────────

    private async Task RunBenchmarkAsync(char driveLetter)
    {
        if (driveLetter == default) return;

        IsBusy = true;
        BenchmarkResults = "Running benchmark...";
        try
        {
            _log.Log($"Starting disk benchmark on {driveLetter}:...");

            var progress = new Progress<string>(msg =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    BenchmarkResults = msg;
                });
            });

            await Task.Run(() => RunBenchmarkCore(driveLetter, progress));

            _log.Log($"Benchmark complete for {driveLetter}:.");
        }
        catch (Exception ex)
        {
            _log.Log($"Benchmark failed: {ex.Message}");
            BenchmarkResults = $"Benchmark failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RunBenchmarkCore(char driveLetter, IProgress<string> progress)
    {
        const int fileSizeMB = 256;
        const int blockSize = 1024 * 1024; // 1 MB
        const int totalBlocks = fileSizeMB;
        const int random4KOps = 500;
        const int random4KBlockSize = 4096;

        string tempPath = Path.Combine($"{driveLetter}:\\", $"pp_bench_{Guid.NewGuid():N}.tmp");
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
                    fs.Write(buffer, 0, blockSize);
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
                while (fs.Read(buffer, 0, blockSize) > 0) { }
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
                    long offset = (long)(rng.NextDouble() * maxOffset);
                    offset = offset - (offset % random4KBlockSize); // align to 4K
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
            try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
        }
    }
}
