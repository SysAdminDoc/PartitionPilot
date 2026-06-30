using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace PartitionPilot;

public class DiskCloningViewModel : ViewModelBase
{
    private readonly ProcessRunner _processRunner;
    private readonly IWmiDiskService _wmiService;
    private readonly ActivityLog _log;
    private readonly IDialogService _dialog;
    private readonly PartitionTableBackup _backup;
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

    private bool _encryptImage;
    public bool EncryptImage
    {
        get => _encryptImage;
        set => SetProperty(ref _encryptImage, value);
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

    // Sector Clone
    private bool _cloneRescueMode;
    public bool CloneRescueMode
    {
        get => _cloneRescueMode;
        set => SetProperty(ref _cloneRescueMode, value);
    }

    private bool _cloneVerify = true;
    public bool CloneVerify
    {
        get => _cloneVerify;
        set => SetProperty(ref _cloneVerify, value);
    }

    private DiskInfo? _cloneSourceDisk;
    public DiskInfo? CloneSourceDisk
    {
        get => _cloneSourceDisk;
        set
        {
            if (SetProperty(ref _cloneSourceDisk, value))
            {
                OnPropertyChanged(nameof(CloneSizeSummary));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private DiskInfo? _cloneDestDisk;
    public DiskInfo? CloneDestDisk
    {
        get => _cloneDestDisk;
        set
        {
            if (SetProperty(ref _cloneDestDisk, value))
            {
                OnPropertyChanged(nameof(CloneSizeSummary));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private string _cloneProgressText = "";
    public string CloneProgressText
    {
        get => _cloneProgressText;
        set => SetProperty(ref _cloneProgressText, value);
    }

    private double _cloneProgressPercent;
    public double CloneProgressPercent
    {
        get => _cloneProgressPercent;
        set => SetProperty(ref _cloneProgressPercent, value);
    }

    public string CloneSizeSummary
    {
        get
        {
            if (CloneSourceDisk is null) return "Select a source disk.";
            if (CloneDestDisk is null) return $"Source: {SizeUtil.Format(CloneSourceDisk.Size)}. Select a destination disk.";
            if (CloneDestDisk.Size < CloneSourceDisk.Size)
                return $"Source: {SizeUtil.Format(CloneSourceDisk.Size)}, Destination: {SizeUtil.Format(CloneDestDisk.Size)} — destination too small.";
            return $"Source: {SizeUtil.Format(CloneSourceDisk.Size)}, Destination: {SizeUtil.Format(CloneDestDisk.Size)} — ready.";
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
    public ICommand SectorCloneCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RefreshCommand { get; }

    public DiskCloningViewModel(ProcessRunner processRunner, IWmiDiskService wmiService, ActivityLog log, IDialogService dialog)
    {
        _processRunner = processRunner;
        _wmiService = wmiService;
        _log = log;
        _dialog = dialog;
        _backup = new PartitionTableBackup(wmiService, log);

        BrowseImageCommand = new WpfRelayCommand(_ => BrowseImagePath());
        BrowseRestoreImageCommand = new WpfRelayCommand(_ => BrowseRestoreImagePath());
        CreateImageCommand = new AsyncRelayCommand(_ => CreateImageAsync(),
            _ => SelectedSourceDrive != default && !string.IsNullOrWhiteSpace(ImagePath));
        RestoreImageCommand = new AsyncRelayCommand(_ => RestoreImageAsync(),
            _ => SelectedTargetDisk is not null && !string.IsNullOrWhiteSpace(RestoreImagePath));
        SectorCloneCommand = new AsyncRelayCommand(_ => SectorCloneAsync(),
            _ => CloneSourceDisk is not null && CloneDestDisk is not null);
        CancelCommand = new WpfRelayCommand(_ => _cts?.Cancel(), _ => IsBusy);
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

            var captureSource = $"{SelectedSourceDrive}:\\";
            VssSnapshot? vssSnapshot = null;
            try
            {
                StatusText = "Checking VSS writer health...";
                await VssSnapshotService.EnsureWritersHealthyAsync(_processRunner, _log, ct);

                StatusText = "Creating VSS snapshot for consistent capture...";
                vssSnapshot = await VssSnapshotService.CreateSnapshotAsync(
                    SelectedSourceDrive, _processRunner, _log, ct);
                captureSource = vssSnapshot.ShadowCopyPath;
                cleanup.Register(
                    $"Delete VSS shadow copy {vssSnapshot.ShadowCopyId}",
                    async () => await vssSnapshot.DisposeAsync(),
                    $"Run vssadmin delete shadows /Shadow={vssSnapshot.ShadowCopyId} /Quiet");
                _log.Log($"Using VSS snapshot {vssSnapshot.ShadowCopyId} for consistent capture.");
            }
            catch (Exception vssEx)
            {
                _log.Log($"VSS snapshot unavailable: {vssEx.Message}");
                if (!_dialog.ConfirmWarning(
                    $"VSS snapshot could not be created:\n{vssEx.Message}\n\n" +
                    "Continue with live volume capture? Files in use may be inconsistent.",
                    "VSS Unavailable"))
                {
                    _log.Log("Image creation cancelled — user declined live capture without VSS.");
                    return;
                }
            }

            var ext = Path.GetExtension(ImagePath).ToLowerInvariant();
            if (ext == ".wim")
            {
                _log.Log($"Creating WIM image of {captureSource} to {ImagePath}...");
                var escapedPath = ProcessRunner.ValidateNativePathArgument(ImagePath);
                await _processRunner.RunExeAsync("dism.exe",
                    $"/Capture-Image /ImageFile:\"{escapedPath}\" /CaptureDir:{captureSource} /Name:\"PartitionPilot Capture\" /Compress:Fast /CheckIntegrity /Verify",
                    _log, ct: ct);
            }
            else
            {
                _log.Log($"Creating VHDX image of {captureSource} to {ImagePath}...");
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

                StatusText = "VHDX created, capturing with robocopy...";
                var safeFileName = ProcessRunner.EscapePowerShellString(Path.GetFileName(ImagePath));
                var letterCmd = $"(Get-Disk | Where-Object {{ $_.Location -like ('*' + {safeFileName} + '*') }} | Get-Partition | Where-Object {{ $_.DriveLetter }} | Select-Object -First 1).DriveLetter";
                var vhdLetter = (await _processRunner.RunPowerShellAsync(letterCmd, _log, ct)).Trim();
                var mountedLetter = RequireDriveLetter(vhdLetter, "mounted VHDX target");

                await _processRunner.RunExeAsync("robocopy", $"{captureSource} {mountedLetter}:\\ /MIR /R:0 /W:0 /NP /NDL /NFL", _log, ignoreStderrOnSuccess: true, ct: ct);

                await _processRunner.RunDiskpartAsync(detachScript, _log, ct);
                detachCleanup.Complete();
            }

            StatusText = "Writing image manifest...";
            var sourceVolume = _volumeByLetter.GetValueOrDefault(char.ToUpperInvariant(SelectedSourceDrive));
            var imageManifest = await DiskImageManifestService.CreateManifestAsync(
                ImagePath,
                SelectedSourceDrive,
                captureSource,
                sourceVolume,
                UpdateService.GetCurrentVersion(),
                _log,
                ct);

            if (vssSnapshot is not null)
            {
                await vssSnapshot.DisposeAsync();
                _log.Log("VSS snapshot cleaned up after successful capture.");
            }

            if (EncryptImage)
            {
                StatusText = "Encrypting image...";
                var password = PromptForInput("Enter encryption password for the disk image:", "Encrypt Image");
                if (string.IsNullOrEmpty(password))
                {
                    _log.Log("Image encryption skipped — no password provided.");
                }
                else
                {
                    var encPath = ImagePath + ".enc";
                    await ImageEncryptionService.EncryptFileAsync(ImagePath, encPath, password, _log, ct: ct);
                    await DiskImageManifestService.RebindManifestToEncryptedImageAsync(imageManifest, encPath, ct);
                    try { File.Delete(DiskImageManifestService.GetManifestPath(ImagePath)); } catch { }
                    try { File.Delete(ImagePath); } catch { }
                    ImagePath = encPath;
                }
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
        var targetIdentity = SelectedTargetDisk.ToIdentitySnapshot();

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
            $"WARNING: Restoring will DESTROY ALL DATA on the target disk.\n\nTarget:\n{targetIdentity.ConfirmationSummary}\n\nContinue?",
            "Confirm Image Restore")) return;

        if (!_dialog.ConfirmDanger(
            "FINAL CONFIRMATION: All data on the target disk will be permanently overwritten.",
            "Confirm Restore")) return;

        string restorePath = RestoreImagePath;
        string? tempDecrypted = null;
        var imageManifestValidation = await ValidateRestoreImageManifestOrConfirmAsync(restorePath, CancellationToken.None);
        if (imageManifestValidation is null)
            return;

        if (ImageEncryptionService.IsEncryptedImage(restorePath))
        {
            var password = PromptForInput("This is an encrypted image. Enter the decryption password:", "Decrypt Image");
            if (string.IsNullOrEmpty(password))
            {
                _dialog.ShowWarning("Restore cancelled — decryption password required.", "Encrypted Image");
                return;
            }
            tempDecrypted = Path.Combine(Path.GetTempPath(), "PartitionPilot",
                Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(restorePath)) +
                Path.GetExtension(Path.GetFileNameWithoutExtension(restorePath)));
            Directory.CreateDirectory(Path.GetDirectoryName(tempDecrypted)!);
            try
            {
                await ImageEncryptionService.DecryptFileAsync(restorePath, tempDecrypted, password, _log);
                if (!await ValidateDecryptedImageHashOrConfirmAsync(tempDecrypted, imageManifestValidation.Manifest, CancellationToken.None))
                    return;
                restorePath = tempDecrypted;
            }
            catch (Exception ex)
            {
                _dialog.ShowError($"Decryption failed: {ex.Message}", "Decrypt Error");
                return;
            }
        }

        if (!await VerifyDiskIdentityBeforeExecuteAsync(targetIdentity, "Restore Target Changed"))
            return;

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
            await targetIdentity.VerifyCurrentAsync(_wmiService);
            if (tempDecrypted is not null)
                cleanup.Register("Delete temporary decrypted image",
                    () => { try { File.Delete(tempDecrypted); } catch { } return Task.CompletedTask; },
                    $"Delete {tempDecrypted}");

            var ext = Path.GetExtension(restorePath).ToLowerInvariant();
            var diskNum = SelectedTargetDisk.Number;
            char? restoredWindowsDrive = null;

            StatusText = "Saving target partition snapshot...";
            await _backup.SaveSnapshotForDestructiveOperationAsync(diskNum, "image restore", ct);

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

                var escapedRestorePath = ProcessRunner.ValidateNativePathArgument(restorePath);
                await _processRunner.RunExeAsync("dism.exe",
                    $"/Apply-Image /ImageFile:\"{escapedRestorePath}\" /ApplyDir:{applyLetter}:\\ /Index:1 /CheckIntegrity /Verify", _log, ct: ct);
                restoredWindowsDrive = applyLetter;
            }
            else
            {
                StatusText = "Mounting VHDX and copying...";
                var mountCmd = $"Mount-DiskImage -ImagePath {ProcessRunner.EscapePowerShellString(restorePath)}";
                await _processRunner.RunPowerShellAsync(mountCmd, _log, ct);

                var unmountCmd = $"Dismount-DiskImage -ImagePath {ProcessRunner.EscapePowerShellString(restorePath)}";
                var mountCleanup = cleanup.Register(
                    $"Dismount restore source image {restorePath}",
                    () => _processRunner.RunPowerShellAsync(unmountCmd, _log),
                    $"Run Dismount-DiskImage for {restorePath} from an elevated PowerShell session.");

                var srcLetterCmd = $"(Get-DiskImage -ImagePath {ProcessRunner.EscapePowerShellString(restorePath)} | Get-Disk | Get-Partition | Where-Object {{ $_.DriveLetter }} | Select-Object -First 1).DriveLetter";
                var srcLetter = (await _processRunner.RunPowerShellAsync(srcLetterCmd, _log, ct)).Trim();
                var sourceLetter = RequireDriveLetter(srcLetter, "mounted source image");

                var partCmd = $"New-Partition -DiskNumber {diskNum} -UseMaximumSize -AssignDriveLetter | Format-Volume -FileSystem NTFS -Confirm:$false";
                await _processRunner.RunPowerShellAsync(partCmd, _log, ct);

                var destLetterCmd = $"(Get-Partition -DiskNumber {diskNum} | Where-Object {{ $_.DriveLetter }} | Select-Object -First 1).DriveLetter";
                var destLetter = (await _processRunner.RunPowerShellAsync(destLetterCmd, _log, ct)).Trim();
                var destinationLetter = RequireDriveLetter(destLetter, "restore destination partition");

                await _processRunner.RunExeAsync("robocopy",
                    $"{sourceLetter}:\\ {destinationLetter}:\\ /MIR /R:0 /W:0 /NP /NDL /NFL", _log, ignoreStderrOnSuccess: true, ct: ct);
                restoredWindowsDrive = destinationLetter;

                await _processRunner.RunPowerShellAsync(unmountCmd, _log, ct);
                mountCleanup.Complete();
            }

            StatusText = "Auditing restored bootability...";
            var bootAudit = await RunBootabilityAuditAsync(diskNum, restoredWindowsDrive, ct);
            var bootAuditReport = bootAudit.FormatReport();
            _log.Log($"Image restored to Disk {diskNum}.");
            _log.Log(bootAuditReport);

            var restoreSummary = $"Image restored successfully to Disk {diskNum}.\n\n{bootAuditReport}";
            if (bootAudit.Status == BootabilityAuditStatus.Pass)
                _dialog.ShowInfo(restoreSummary, "Restore Complete");
            else
                _dialog.ShowWarning(restoreSummary, "Restore Complete (Boot Audit Warnings)");
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

    private async Task SectorCloneAsync()
    {
        if (CloneSourceDisk is null || CloneDestDisk is null) return;
        var sourceIdentity = CloneSourceDisk.ToIdentitySnapshot();
        var destIdentity = CloneDestDisk.ToIdentitySnapshot();

        try
        {
            SectorCloneService.ValidateClone(CloneSourceDisk, CloneDestDisk);
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message, "Clone Validation Failed");
            return;
        }

        var protectedTargets = await GetBitLockerProtectedTargetsAsync(CloneDestDisk.Number);
        if (protectedTargets.Count > 0 &&
            !_dialog.ConfirmDanger(
                BitLockerPreflight.BuildDestructiveConfirmation(
                    $"Sector clone to Disk {CloneDestDisk.Number}", protectedTargets),
                "Confirm BitLocker-Protected Clone"))
            return;

        if (!_dialog.ConfirmDanger(
            $"WARNING: This will overwrite ALL data on the destination disk with a sector-by-sector copy.\n\nSource:\n{sourceIdentity.ConfirmationSummary}\n\nDestination:\n{destIdentity.ConfirmationSummary}\n\nThis operation cannot be undone. Continue?",
            "Confirm Sector Clone"))
            return;

        if (!_dialog.ConfirmDanger(
            "FINAL CONFIRMATION: All data on the destination disk will be permanently overwritten with a raw sector copy.",
            "Confirm Clone"))
            return;

        if (!await VerifyDiskIdentityBeforeExecuteAsync(sourceIdentity, "Clone Source Changed"))
            return;
        if (!await VerifyDiskIdentityBeforeExecuteAsync(destIdentity, "Clone Destination Changed"))
            return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsBusy = true;
        StatusText = $"Cloning Disk {CloneSourceDisk.Number} -> Disk {CloneDestDisk.Number}...";
        CloneProgressText = "Starting sector clone...";
        CloneProgressPercent = 0;

        var targetLocks = new List<VolumeLock>();
        try
        {
            await sourceIdentity.VerifyCurrentAsync(_wmiService);
            await destIdentity.VerifyCurrentAsync(_wmiService);

            StatusText = "Saving destination partition snapshot...";
            await _backup.SaveSnapshotForDestructiveOperationAsync(CloneDestDisk.Number, "sector clone", ct);

            var targetPartitions = await _wmiService.GetPartitionsAsync(CloneDestDisk.Number);
            targetLocks = targetPartitions
                .Where(p => p.DriveLetter.HasValue)
                .Select(p => VolumeLockService.RequireLock(p.DriveLetter!.Value, _log))
                .ToList();

            var progress = new Progress<SectorCloneProgress>(p =>
            {
                CloneProgressText = $"{p.ProgressText}  {p.RateText}  ETA: {p.EstimatedRemaining:hh\\:mm\\:ss}";
                CloneProgressPercent = p.PercentComplete;
                StatusText = $"Cloning... {p.PercentComplete:F1}%";
            });

            var cloneResult = await SectorCloneService.CloneAsync(
                CloneSourceDisk.Number, CloneDestDisk.Number, CloneSourceDisk.Size,
                _log, progress, ct, rescue: CloneRescueMode, verify: CloneVerify);

            CloneProgressPercent = 100;
            StatusText = "Auditing cloned bootability...";
            var bootAudit = await RunBootabilityAuditAsync(CloneDestDisk.Number, null, ct);
            var bootAuditReport = bootAudit.FormatReport();
            _log.Log(bootAuditReport);
            CloneProgressText = $"{cloneResult.FormatReport()}\n\n{bootAuditReport}";

            var summary = $"Sector clone complete.\n\nDisk {CloneSourceDisk.Number} -> Disk {CloneDestDisk.Number}\n{cloneResult.FormatReport()}\n\n{bootAuditReport}";
            if (cloneResult.HasBadSectors || !cloneResult.VerificationPassed || bootAudit.Status != BootabilityAuditStatus.Pass)
                _dialog.ShowWarning(summary, "Clone Complete (With Warnings)");
            else
                _dialog.ShowInfo(summary, "Clone Complete");
        }
        catch (OperationCanceledException)
        {
            _log.Log("Sector clone cancelled.");
            CloneProgressText = "Clone cancelled.";
        }
        catch (Exception ex)
        {
            _log.Log($"Sector clone failed: {ex.Message}");
            _dialog.ShowError($"Sector clone failed:\n{ex.Message}", "Clone Error");
            CloneProgressText = $"Failed: {ex.Message}";
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

    private Task<List<string>> GetBitLockerProtectedTargetsAsync(int diskNumber) =>
        _wmiService.GetBitLockerProtectedTargetsAsync(diskNumber);

    private async Task<DiskImageManifestValidation?> ValidateRestoreImageManifestOrConfirmAsync(string imagePath, CancellationToken ct)
    {
        var validation = await DiskImageManifestService.ValidateManifestAsync(imagePath, ct);
        if (validation.IsValid)
        {
            _log.Log($"Image manifest validated for restore: {imagePath}");
            return validation;
        }

        _log.Log($"Image manifest validation failed/degraded: {validation.Status} - {validation.Detail}");
        return _dialog.ConfirmWarning(
            $"{validation.Detail}\n\nContinue restore in degraded mode? The target disk will still be cleared if you proceed.",
            "Image Manifest Verification")
            ? validation
            : null;
    }

    private async Task<BootabilityAuditReport> RunBootabilityAuditAsync(int diskNumber, char? knownWindowsDrive, CancellationToken ct)
    {
        try
        {
            return await BootabilityAuditService.AuditAsync(
                diskNumber,
                _wmiService,
                _processRunner,
                _log,
                knownWindowsDrive,
                ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Log($"Bootability audit failed: {ex.Message}");
            return new BootabilityAuditReport
            {
                DiskNumber = diskNumber,
                Status = BootabilityAuditStatus.Warning,
                SuggestedBootRepairPlan = $"Run `pp boot-audit --disk {diskNumber}` after refreshing disk inventory.",
                Issues =
                {
                    new BootabilityAuditIssue
                    {
                        Severity = BootabilityAuditStatus.Warning,
                        Code = "BootAuditFailed",
                        Message = $"Bootability audit could not complete: {ex.Message}",
                        Remediation = "Refresh disks and rerun the boot audit from the CLI or Disk Cloning workflow."
                    }
                }
            };
        }
    }

    private async Task<bool> ValidateDecryptedImageHashOrConfirmAsync(string decryptedPath, DiskImageManifest? manifest, CancellationToken ct)
    {
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.PlainImageSha256))
            return true;

        var actual = await DiskImageManifestService.ComputeSha256HexAsync(decryptedPath, ct);
        if (string.Equals(actual, manifest.PlainImageSha256, StringComparison.OrdinalIgnoreCase))
        {
            _log.Log("Decrypted image hash matches manifest plain-image hash.");
            return true;
        }

        _log.Log($"Decrypted image hash mismatch. Expected {manifest.PlainImageSha256}, got {actual}.");
        return _dialog.ConfirmWarning(
            $"The decrypted image hash does not match the manifest.\n\nExpected: {manifest.PlainImageSha256}\nActual: {actual}\n\nContinue restore in degraded mode?",
            "Decrypted Image Verification");
    }

    private async Task<bool> VerifyDiskIdentityBeforeExecuteAsync(DiskIdentitySnapshot identity, string title)
    {
        try
        {
            await identity.VerifyCurrentAsync(_wmiService);
            return true;
        }
        catch (Exception ex)
        {
            _log.Log($"Target identity check failed: {ex.Message}");
            _dialog.ShowError(ex.Message, title);
            return false;
        }
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

    private static string? PromptForInput(string message, string title)
    {
        var dialog = new System.Windows.Window
        {
            Title = title,
            Width = 400,
            SizeToContent = System.Windows.SizeToContent.Height,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            ResizeMode = System.Windows.ResizeMode.NoResize,
            Owner = System.Windows.Application.Current.MainWindow
        };

        var passwordBox = new System.Windows.Controls.PasswordBox { Margin = new System.Windows.Thickness(12), Height = 30 };
        var okButton = new System.Windows.Controls.Button { Content = "OK", Width = 80, Height = 30, IsDefault = true, Margin = new System.Windows.Thickness(0, 0, 8, 12) };
        var cancelButton = new System.Windows.Controls.Button { Content = "Cancel", Width = 80, Height = 30, IsCancel = true, Margin = new System.Windows.Thickness(0, 0, 12, 12) };

        string? result = null;
        okButton.Click += (_, _) => { result = passwordBox.Password; dialog.DialogResult = true; };

        var buttonPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        var panel = new System.Windows.Controls.StackPanel();
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = message, Margin = new System.Windows.Thickness(12, 12, 12, 4), TextWrapping = System.Windows.TextWrapping.Wrap });
        panel.Children.Add(passwordBox);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;
        return dialog.ShowDialog() == true ? result : null;
    }

    public sealed record ImageDestinationPreflight(
        string FullPath,
        string DestinationRoot,
        long EstimatedRequiredBytes,
        long DestinationFreeBytes);
}
