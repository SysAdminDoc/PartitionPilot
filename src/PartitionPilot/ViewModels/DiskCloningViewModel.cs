using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace PartitionPilot;

public class DiskCloningViewModel : ViewModelBase
{
    private readonly ProcessRunner _processRunner;
    private readonly WmiDiskService _wmiService;
    private readonly ActivityLog _log;
    private readonly IDialogService _dialog;

    public ObservableCollection<DiskInfo> AllDisks { get; } = new();
    public ObservableCollection<char> DriveLetters { get; } = new();

    // Create Image
    private char _selectedSourceDrive;
    public char SelectedSourceDrive
    {
        get => _selectedSourceDrive;
        set
        {
            if (SetProperty(ref _selectedSourceDrive, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private string _imagePath = "";
    public string ImagePath
    {
        get => _imagePath;
        set
        {
            if (SetProperty(ref _imagePath, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    // Restore Image
    private string _restoreImagePath = "";
    public string RestoreImagePath
    {
        get => _restoreImagePath;
        set
        {
            if (SetProperty(ref _restoreImagePath, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private DiskInfo? _selectedTargetDisk;
    public DiskInfo? SelectedTargetDisk
    {
        get => _selectedTargetDisk;
        set
        {
            if (SetProperty(ref _selectedTargetDisk, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

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

    private CancellationTokenSource? _cts;

    public ICommand BrowseImageCommand { get; }
    public ICommand BrowseRestoreImageCommand { get; }
    public ICommand CreateImageCommand { get; }
    public ICommand RestoreImageCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RefreshCommand { get; }

    public DiskCloningViewModel(ProcessRunner processRunner, WmiDiskService wmiService, ActivityLog log, IDialogService dialog)
    {
        _processRunner = processRunner;
        _wmiService = wmiService;
        _log = log;
        _dialog = dialog;

        BrowseImageCommand = new RelayCommand(_ => BrowseImagePath());
        BrowseRestoreImageCommand = new RelayCommand(_ => BrowseRestoreImagePath());
        CreateImageCommand = new AsyncRelayCommand(_ => CreateImageAsync(),
            _ => SelectedSourceDrive != default && !string.IsNullOrWhiteSpace(ImagePath));
        RestoreImageCommand = new AsyncRelayCommand(_ => RestoreImageAsync(),
            _ => SelectedTargetDisk is not null && !string.IsNullOrWhiteSpace(RestoreImagePath));
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsBusy);
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
    }

    public async Task RefreshAsync()
    {
        var disks = await _wmiService.GetDisksAsync();
        var volumes = await _wmiService.GetVolumesAsync();
        var letters = volumes
            .Where(v => v.DriveLetter.HasValue)
            .Select(v => v.DriveLetter!.Value)
            .OrderBy(c => c)
            .ToList();

        Application.Current.Dispatcher.Invoke(() =>
        {
            AllDisks.Clear();
            foreach (var d in disks) AllDisks.Add(d);
            DriveLetters.Clear();
            foreach (var l in letters) DriveLetters.Add(l);
        });
    }

    private void BrowseImagePath()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save Disk Image",
            Filter = "VHDX Files (*.vhdx)|*.vhdx|WIM Files (*.wim)|*.wim",
            DefaultExt = ".vhdx"
        };
        if (dlg.ShowDialog() == true) ImagePath = dlg.FileName;
    }

    private void BrowseRestoreImagePath()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Image to Restore",
            Filter = "Disk Images (*.vhdx;*.wim)|*.vhdx;*.wim|All Files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() == true) RestoreImagePath = dlg.FileName;
    }

    private async Task CreateImageAsync()
    {
        if (SelectedSourceDrive == default || string.IsNullOrWhiteSpace(ImagePath)) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsBusy = true;
        StatusText = $"Creating image of {SelectedSourceDrive}:\\...";

        try
        {
            var ext = Path.GetExtension(ImagePath).ToLowerInvariant();
            if (ext == ".wim")
            {
                _log.Log($"Creating WIM image of {SelectedSourceDrive}:\\ to {ImagePath}...");
                await _processRunner.RunExeAsync("dism.exe",
                    $"/Capture-Image /ImageFile:\"{ImagePath}\" /CaptureDir:{SelectedSourceDrive}:\\ /Name:\"PartitionPilot Capture\" /Compress:Fast",
                    _log, ct: ct);
            }
            else
            {
                _log.Log($"Creating VHDX image of {SelectedSourceDrive}:\\ to {ImagePath}...");
                var sizeCmd = $"(Get-Partition -DriveLetter '{SelectedSourceDrive}' | Select-Object -ExpandProperty Size)";
                var sizeResult = await _processRunner.RunPowerShellAsync(sizeCmd, _log, ct);
                var sizeMB = long.TryParse(sizeResult.Trim(), out var sizeBytes) ? sizeBytes / (1024 * 1024) + 100 : 50000;

                var script = $"""
                    create vdisk file="{ImagePath}" maximum={sizeMB} type=expandable
                    select vdisk file="{ImagePath}"
                    attach vdisk
                    """;
                await _processRunner.RunDiskpartAsync(script, _log, ct);

                StatusText = "VHDX created, capturing with DISM...";
                var letterCmd = "(Get-Disk | Where-Object { $_.Location -like '*" + Path.GetFileName(ImagePath) + "*' } | Get-Partition | Where-Object { $_.DriveLetter } | Select-Object -First 1).DriveLetter";
                var vhdLetter = (await _processRunner.RunPowerShellAsync(letterCmd, _log, ct)).Trim();

                if (!string.IsNullOrEmpty(vhdLetter) && char.IsLetter(vhdLetter[0]))
                {
                    await _processRunner.RunExeAsync("robocopy", $"{SelectedSourceDrive}:\\ {vhdLetter}:\\ /MIR /R:0 /W:0 /NP /NDL /NFL", _log, ignoreStderrOnSuccess: true, ct: ct);
                }

                var detachScript = $"""
                    select vdisk file="{ImagePath}"
                    detach vdisk
                    """;
                await _processRunner.RunDiskpartAsync(detachScript, _log, ct);
            }

            _log.Log($"Image created: {ImagePath}");
            _dialog.ShowInfo($"Disk image created successfully.\n\nPath: {ImagePath}", "Image Created");
        }
        catch (OperationCanceledException)
        {
            _log.Log("Image creation cancelled.");
        }
        catch (Exception ex)
        {
            _log.Log($"Image creation failed: {ex.Message}");
            _dialog.ShowError($"Image creation failed:\n{ex.Message}", "Create Image Error");
        }
        finally
        {
            IsBusy = false;
            StatusText = "";
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task RestoreImageAsync()
    {
        if (SelectedTargetDisk is null || string.IsNullOrWhiteSpace(RestoreImagePath)) return;

        if (!_dialog.ConfirmDanger(
            $"WARNING: Restoring to Disk {SelectedTargetDisk.Number} ({SelectedTargetDisk.FriendlyName}) will DESTROY ALL DATA on the target disk.\n\nContinue?",
            "Confirm Image Restore")) return;

        if (!_dialog.ConfirmDanger(
            "FINAL CONFIRMATION: All data on the target disk will be permanently overwritten.",
            "Confirm Restore")) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsBusy = true;
        StatusText = $"Restoring image to Disk {SelectedTargetDisk.Number}...";

        try
        {
            var ext = Path.GetExtension(RestoreImagePath).ToLowerInvariant();
            var diskNum = SelectedTargetDisk.Number;

            StatusText = "Clearing target disk...";
            var clearCmd = $"Clear-Disk -Number {diskNum} -RemoveData -RemoveOEM -Confirm:$false";
            await _processRunner.RunPowerShellAsync(clearCmd, _log, ct);

            var initCmd = $"Initialize-Disk -Number {diskNum} -PartitionStyle GPT -Confirm:$false";
            await _processRunner.RunPowerShellAsync(initCmd, _log, ct);

            if (ext == ".wim")
            {
                StatusText = "Creating partition and applying WIM...";
                var partCmd = $"New-Partition -DiskNumber {diskNum} -UseMaximumSize -AssignDriveLetter | Format-Volume -FileSystem NTFS -Confirm:$false";
                await _processRunner.RunPowerShellAsync(partCmd, _log, ct);

                var letterCmd = $"(Get-Partition -DiskNumber {diskNum} | Where-Object {{ $_.DriveLetter }} | Select-Object -First 1).DriveLetter";
                var targetLetter = (await _processRunner.RunPowerShellAsync(letterCmd, _log, ct)).Trim();

                if (string.IsNullOrEmpty(targetLetter) || !char.IsLetter(targetLetter[0]))
                    throw new InvalidOperationException("Could not assign a drive letter to the target partition.");

                await _processRunner.RunExeAsync("dism.exe",
                    $"/Apply-Image /ImageFile:\"{RestoreImagePath}\" /ApplyDir:{targetLetter}:\\ /Index:1", _log, ct: ct);
            }
            else
            {
                StatusText = "Mounting VHDX and copying...";
                var mountCmd = $"Mount-DiskImage -ImagePath '{RestoreImagePath.Replace("'", "''")}'";
                await _processRunner.RunPowerShellAsync(mountCmd, _log, ct);

                var srcLetterCmd = $"(Get-DiskImage -ImagePath '{RestoreImagePath.Replace("'", "''")}' | Get-Disk | Get-Partition | Where-Object {{ $_.DriveLetter }} | Select-Object -First 1).DriveLetter";
                var srcLetter = (await _processRunner.RunPowerShellAsync(srcLetterCmd, _log, ct)).Trim();

                var partCmd = $"New-Partition -DiskNumber {diskNum} -UseMaximumSize -AssignDriveLetter | Format-Volume -FileSystem NTFS -Confirm:$false";
                await _processRunner.RunPowerShellAsync(partCmd, _log, ct);

                var destLetterCmd = $"(Get-Partition -DiskNumber {diskNum} | Where-Object {{ $_.DriveLetter }} | Select-Object -First 1).DriveLetter";
                var destLetter = (await _processRunner.RunPowerShellAsync(destLetterCmd, _log, ct)).Trim();

                if (!string.IsNullOrEmpty(srcLetter) && !string.IsNullOrEmpty(destLetter))
                {
                    await _processRunner.RunExeAsync("robocopy",
                        $"{srcLetter}:\\ {destLetter}:\\ /MIR /R:0 /W:0 /NP /NDL /NFL", _log, ignoreStderrOnSuccess: true, ct: ct);
                }

                var unmountCmd = $"Dismount-DiskImage -ImagePath '{RestoreImagePath.Replace("'", "''")}'";
                await _processRunner.RunPowerShellAsync(unmountCmd, _log, ct);
            }

            _log.Log($"Image restored to Disk {diskNum}.");
            _dialog.ShowInfo($"Image restored successfully to Disk {diskNum}.", "Restore Complete");
        }
        catch (OperationCanceledException)
        {
            _log.Log("Image restore cancelled.");
        }
        catch (Exception ex)
        {
            _log.Log($"Image restore failed: {ex.Message}");
            _dialog.ShowError($"Image restore failed:\n{ex.Message}", "Restore Error");
        }
        finally
        {
            IsBusy = false;
            StatusText = "";
            _cts?.Dispose();
            _cts = null;
        }
    }
}
