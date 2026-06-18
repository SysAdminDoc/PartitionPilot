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
    private readonly Dictionary<char, VolumeInfo> _volumeByLetter = new();
    private readonly Dictionary<char, string> _sourceBitLockerByLetter = new();

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
            {
                UpdateImagePreflightSummary();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private string _imagePath = "";
    public string ImagePath
    {
        get => _imagePath;
        set
        {
            if (SetProperty(ref _imagePath, value))
            {
                UpdateImagePreflightSummary();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private string _imagePreflightSummary = "Choose a source volume and destination path to check free space before capture.";
    public string ImagePreflightSummary
    {
        get => _imagePreflightSummary;
        set => SetProperty(ref _imagePreflightSummary, value);
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
        var bitlockerStatus = await _wmiService.GetBitLockerStatusAsync();
        var letters = volumes
            .Where(v => v.DriveLetter.HasValue)
            .Select(v => v.DriveLetter!.Value)
            .OrderBy(c => c)
            .ToList();

        Application.Current.Dispatcher.Invoke(() =>
        {
            AllDisks.Clear();
            foreach (var d in disks) AllDisks.Add(d);

            _volumeByLetter.Clear();
            foreach (var v in volumes.Where(v => v.DriveLetter.HasValue))
            {
                if (bitlockerStatus.TryGetValue(v.DriveLetter!.Value, out var encryptionStatus))
                    v.EncryptionStatus = encryptionStatus;
                _volumeByLetter[char.ToUpperInvariant(v.DriveLetter!.Value)] = v;
            }

            _sourceBitLockerByLetter.Clear();
            foreach (var pair in bitlockerStatus)
                _sourceBitLockerByLetter[char.ToUpperInvariant(pair.Key)] = pair.Value;

            DriveLetters.Clear();
            foreach (var l in letters) DriveLetters.Add(l);
            UpdateImagePreflightSummary();
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

        ImageDestinationPreflight preflight;
        try
        {
            GuardSourceVolumeForImageCapture(SelectedSourceDrive);
            preflight = PreflightSelectedImageDestination();
        }
        catch (Exception ex)
        {
            _log.Log($"Image creation preflight failed: {ex.Message}");
            _dialog.ShowError($"Image creation cannot start:\n{ex.Message}", "Create Image Preflight");
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsBusy = true;
        StatusText = $"Creating image of {SelectedSourceDrive}:\\...";

        try
        {
            await using var cleanup = new OperationCleanupScope(_log);
            ImagePath = preflight.FullPath;
            _log.Log(
                $"Image destination preflight passed: required {SizeUtil.Format(preflight.EstimatedRequiredBytes)}, destination free {SizeUtil.Format(preflight.DestinationFreeBytes)} on {preflight.DestinationRoot}.");

            var ext = Path.GetExtension(ImagePath).ToLowerInvariant();
            if (ext == ".wim")
            {
                _log.Log($"Creating WIM image of {SelectedSourceDrive}:\\ to {ImagePath}...");
                var escapedPath = ProcessRunner.ValidateNativePathArgument(ImagePath);
                await _processRunner.RunExeAsync("dism.exe",
                    $"/Capture-Image /ImageFile:\"{escapedPath}\" /CaptureDir:{SelectedSourceDrive}:\\ /Name:\"PartitionPilot Capture\" /Compress:Fast",
                    _log, ct: ct);
            }
            else
            {
                _log.Log($"Creating VHDX image of {SelectedSourceDrive}:\\ to {ImagePath}...");
                var sizeCmd = $"(Get-Partition -DriveLetter '{SelectedSourceDrive}' | Select-Object -ExpandProperty Size)";
                var sizeResult = await _processRunner.RunPowerShellAsync(sizeCmd, _log, ct);
                var sizeMB = long.TryParse(sizeResult.Trim(), out var sizeBytes) ? sizeBytes / (1024 * 1024) + 100 : 50000;

                var sanitizedImagePath = ProcessRunner.ValidateNativePathArgument(ImagePath);
                var script = $"""
                    create vdisk file="{sanitizedImagePath}" maximum={sizeMB} type=expandable
                    select vdisk file="{sanitizedImagePath}"
                    attach vdisk
                    """;
                await _processRunner.RunDiskpartAsync(script, _log, ct);

                var detachScript = $"""
                    select vdisk file="{sanitizedImagePath}"
                    detach vdisk
                    """;
                var detachCleanup = cleanup.Register(
                    $"Detach temporary VHDX target {ImagePath}",
                    () => _processRunner.RunDiskpartAsync(detachScript, _log),
                    $"Run diskpart, select vdisk file=\"{sanitizedImagePath}\", then detach vdisk.");

                StatusText = "VHDX created, capturing with DISM...";
                var safeFileName = ProcessRunner.EscapePowerShellString(Path.GetFileName(ImagePath));
                var letterCmd = $"(Get-Disk | Where-Object {{ $_.Location -like ('*' + {safeFileName} + '*') }} | Get-Partition | Where-Object {{ $_.DriveLetter }} | Select-Object -First 1).DriveLetter";
                var vhdLetter = (await _processRunner.RunPowerShellAsync(letterCmd, _log, ct)).Trim();
                var mountedLetter = RequireDriveLetter(vhdLetter, "mounted VHDX target");

                await _processRunner.RunExeAsync("robocopy", $"{SelectedSourceDrive}:\\ {mountedLetter}:\\ /MIR /R:0 /W:0 /NP /NDL /NFL", _log, ignoreStderrOnSuccess: true, ct: ct);

                await _processRunner.RunDiskpartAsync(detachScript, _log, ct);
                detachCleanup.Complete();
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

        var protectedTargets = await GetBitLockerProtectedTargetsAsync(SelectedTargetDisk.Number);
        if (protectedTargets.Count > 0 &&
            !_dialog.ConfirmDanger(
                BitLockerPreflight.BuildDestructiveConfirmation(
                    $"Restore image to Disk {SelectedTargetDisk.Number}",
                    protectedTargets),
                "Confirm BitLocker-Protected Restore"))
        {
            return;
        }

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

        var targetLocks = new List<VolumeLock>();
        try
        {
            await using var cleanup = new OperationCleanupScope(_log);
            var ext = Path.GetExtension(RestoreImagePath).ToLowerInvariant();
            var diskNum = SelectedTargetDisk.Number;

            // Best-effort lock volumes on target disk before clearing
            var targetPartitions = await _wmiService.GetPartitionsAsync(diskNum);
            targetLocks = targetPartitions
                .Where(p => p.DriveLetter.HasValue)
                .Select(p => VolumeLockService.RequireLock(p.DriveLetter!.Value, _log))
                .ToList();

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
                var applyLetter = RequireDriveLetter(targetLetter, "target partition");

                var escapedRestorePath = ProcessRunner.ValidateNativePathArgument(RestoreImagePath);
                await _processRunner.RunExeAsync("dism.exe",
                    $"/Apply-Image /ImageFile:\"{escapedRestorePath}\" /ApplyDir:{applyLetter}:\\ /Index:1", _log, ct: ct);
            }
            else
            {
                StatusText = "Mounting VHDX and copying...";
                var mountCmd = $"Mount-DiskImage -ImagePath {ProcessRunner.EscapePowerShellString(RestoreImagePath)}";
                await _processRunner.RunPowerShellAsync(mountCmd, _log, ct);

                var unmountCmd = $"Dismount-DiskImage -ImagePath {ProcessRunner.EscapePowerShellString(RestoreImagePath)}";
                var mountCleanup = cleanup.Register(
                    $"Dismount restore source image {RestoreImagePath}",
                    () => _processRunner.RunPowerShellAsync(unmountCmd, _log),
                    $"Run Dismount-DiskImage for {RestoreImagePath} from an elevated PowerShell session.");

                var srcLetterCmd = $"(Get-DiskImage -ImagePath {ProcessRunner.EscapePowerShellString(RestoreImagePath)} | Get-Disk | Get-Partition | Where-Object {{ $_.DriveLetter }} | Select-Object -First 1).DriveLetter";
                var srcLetter = (await _processRunner.RunPowerShellAsync(srcLetterCmd, _log, ct)).Trim();
                var sourceLetter = RequireDriveLetter(srcLetter, "mounted source image");

                var partCmd = $"New-Partition -DiskNumber {diskNum} -UseMaximumSize -AssignDriveLetter | Format-Volume -FileSystem NTFS -Confirm:$false";
                await _processRunner.RunPowerShellAsync(partCmd, _log, ct);

                var destLetterCmd = $"(Get-Partition -DiskNumber {diskNum} | Where-Object {{ $_.DriveLetter }} | Select-Object -First 1).DriveLetter";
                var destLetter = (await _processRunner.RunPowerShellAsync(destLetterCmd, _log, ct)).Trim();
                var destinationLetter = RequireDriveLetter(destLetter, "restore destination partition");

                await _processRunner.RunExeAsync("robocopy",
                    $"{sourceLetter}:\\ {destinationLetter}:\\ /MIR /R:0 /W:0 /NP /NDL /NFL", _log, ignoreStderrOnSuccess: true, ct: ct);

                await _processRunner.RunPowerShellAsync(unmountCmd, _log, ct);
                mountCleanup.Complete();
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
            foreach (var l in targetLocks) l?.Dispose();
            IsBusy = false;
            StatusText = "";
            _cts?.Dispose();
            _cts = null;
        }
    }

    public static char RequireDriveLetter(string? value, string context)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length != 1 || !char.IsLetter(trimmed[0]))
            throw new InvalidOperationException($"Could not resolve a drive letter for the {context}.");

        return char.ToUpperInvariant(trimmed[0]);
    }

    private void UpdateImagePreflightSummary()
    {
        if (SelectedSourceDrive == default)
        {
            ImagePreflightSummary = "Choose a source volume to estimate image size.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ImagePath))
        {
            ImagePreflightSummary = "Choose a destination path to check available free space.";
            return;
        }

        try
        {
            var preflight = PreflightSelectedImageDestination();
            ImagePreflightSummary =
                $"Estimated required: {SizeUtil.Format(preflight.EstimatedRequiredBytes)}. Destination free: {SizeUtil.Format(preflight.DestinationFreeBytes)}.";
        }
        catch (Exception ex)
        {
            ImagePreflightSummary = $"Destination check: {ex.Message}";
        }
    }

    private ImageDestinationPreflight PreflightSelectedImageDestination()
    {
        var requiredBytes = EstimateSelectedImageBytes();
        return PreflightImageDestination(
            ImagePath,
            SelectedSourceDrive,
            requiredBytes,
            Directory.Exists,
            File.Exists,
            root => new DriveInfo(root).AvailableFreeSpace);
    }

    private long EstimateSelectedImageBytes()
    {
        return _volumeByLetter.TryGetValue(char.ToUpperInvariant(SelectedSourceDrive), out var volume)
            ? EstimateImageBytes(volume.Size, volume.SizeRemaining)
            : 0;
    }

    private void GuardSourceVolumeForImageCapture(char sourceDrive)
    {
        var key = char.ToUpperInvariant(sourceDrive);
        if (!_sourceBitLockerByLetter.TryGetValue(key, out var status) || !BitLockerPreflight.RequiresUnlockForRead(status))
            return;

        throw new InvalidOperationException(
            BitLockerPreflight.BuildUnlockRequiredMessage($"Create an image from {key}:", $"{key}:", status));
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

    public static long EstimateImageBytes(long sourceSizeBytes, long sourceFreeBytes)
    {
        if (sourceSizeBytes <= 0) return 0;

        var hasUsableFreeSpace = sourceFreeBytes >= 0 && sourceFreeBytes <= sourceSizeBytes;
        var usedBytes = hasUsableFreeSpace ? sourceSizeBytes - sourceFreeBytes : sourceSizeBytes;
        var minimumImageBytes = 1L << 30;
        var overheadBytes = 512L * 1024L * 1024L;
        var estimatedBytes = Math.Max(usedBytes, minimumImageBytes);

        return estimatedBytes > long.MaxValue - overheadBytes
            ? long.MaxValue
            : estimatedBytes + overheadBytes;
    }

    public static ImageDestinationPreflight PreflightImageDestination(
        string imagePath,
        char sourceDrive,
        long estimatedRequiredBytes,
        Func<string, bool> directoryExists,
        Func<string, bool> fileExists,
        Func<string, long> getAvailableFreeSpace)
    {
        if (sourceDrive == default)
            throw new InvalidOperationException("Select a source volume before creating an image.");

        if (string.IsNullOrWhiteSpace(imagePath))
            throw new InvalidOperationException("Choose a destination path before creating an image.");

        var trimmedPath = imagePath.Trim();
        if (!Path.IsPathFullyQualified(trimmedPath))
            throw new InvalidOperationException("Choose a fully qualified destination path.");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(trimmedPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException($"The destination path is invalid: {ex.Message}", ex);
        }

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is not ".wim" and not ".vhdx")
            throw new InvalidOperationException("Choose a .wim or .vhdx destination file.");

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root) || !directoryExists(root))
            throw new InvalidOperationException("The destination drive or share is not available.");

        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(parent) || !directoryExists(parent))
            throw new InvalidOperationException("Create the destination folder before starting the image capture.");

        if (fileExists(fullPath))
            throw new InvalidOperationException("Choose a new image path or delete the existing file first.");

        if (TryGetDriveLetter(root) is { } destinationDrive &&
            char.ToUpperInvariant(destinationDrive) == char.ToUpperInvariant(sourceDrive))
        {
            throw new InvalidOperationException("Choose a destination outside the source volume; capturing a volume into itself is unsafe.");
        }

        long availableBytes;
        try
        {
            availableBytes = getAvailableFreeSpace(root);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not read free space for the destination: {ex.Message}", ex);
        }

        if (estimatedRequiredBytes > 0 && availableBytes < estimatedRequiredBytes)
        {
            throw new InvalidOperationException(
                $"The destination has {SizeUtil.Format(availableBytes)} free, but the image may require up to {SizeUtil.Format(estimatedRequiredBytes)}.");
        }

        return new ImageDestinationPreflight(fullPath, root, Math.Max(estimatedRequiredBytes, 0), availableBytes);
    }

    private static char? TryGetDriveLetter(string root)
    {
        return root.Length >= 2 && root[1] == ':' && char.IsLetter(root[0])
            ? char.ToUpperInvariant(root[0])
            : null;
    }

    public sealed record ImageDestinationPreflight(
        string FullPath,
        string DestinationRoot,
        long EstimatedRequiredBytes,
        long DestinationFreeBytes);
}
