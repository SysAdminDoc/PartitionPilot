using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly IShadowCopyProvider _shadowCopyProvider = new WmiShadowCopyProvider();
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

    private string _imagePreflightSummary = LocExtension.Get("ImagePreflightPrompt");
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
            if (CloneSourceDisk is null) return LocExtension.Get("CloneSelectSource");
            if (CloneDestDisk is null)
                return LocExtension.Format("CloneSelectDest", SizeUtil.Format(CloneSourceDisk.Size));

            return LocExtension.Format(
                CloneDestDisk.Size < CloneSourceDisk.Size ? "CloneDestTooSmall" : "CloneReady",
                SizeUtil.Format(CloneSourceDisk.Size), SizeUtil.Format(CloneDestDisk.Size));
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
            Title = LocExtension.Get("SaveDiskImageTitle"),
            Filter = LocExtension.Get("SaveDiskImageFilter"),
            DefaultExt = ".vhdx"
        };
        if (dlg.ShowDialog() == true) ImagePath = dlg.FileName;
    }

    private void BrowseRestoreImagePath()
    {
        var dlg = new OpenFileDialog
        {
            Title = LocExtension.Get("SelectImageTitle"),
            Filter = LocExtension.Get("SelectImageFilter"),
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
            _dialog.ShowError(
                LocExtension.Format("ImageCreationCannotStart", ex.Message),
                LocExtension.Get("CreateImagePreflightTitle"));
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsBusy = true;
        StatusText = LocExtension.Format("StatusCreatingImage", SelectedSourceDrive);

        try
        {
            await using var cleanup = new OperationCleanupScope(_log);
            ImagePath = preflight.FullPath;
            _log.Log(
                $"Image destination preflight passed: required {SizeUtil.Format(preflight.EstimatedRequiredBytes)}, destination free {SizeUtil.Format(preflight.DestinationFreeBytes)} on {preflight.DestinationRoot}.");

            var captureSource = $"{SelectedSourceDrive}:\\";
            VssSnapshot? vssSnapshot = null;
            OperationCleanupScope.CleanupRegistration? vssCleanup = null;
            try
            {
                StatusText = LocExtension.Get("StatusCheckingVss");
                await VssSnapshotService.EnsureWritersHealthyAsync(_processRunner, _log, ct);

                StatusText = LocExtension.Get("StatusCreatingVss");
                vssSnapshot = await VssSnapshotService.CreateSnapshotAsync(
                    SelectedSourceDrive, _shadowCopyProvider, _log, ct);
                captureSource = vssSnapshot.ShadowCopyPath;
                vssCleanup = cleanup.Register(
                    $"Delete VSS shadow copy {vssSnapshot.ShadowCopyId}",
                    async () => await vssSnapshot.DisposeAsync(),
                    $"Run vssadmin delete shadows /Shadow={vssSnapshot.ShadowCopyId} /Quiet");
                _log.Log($"Using VSS snapshot {vssSnapshot.ShadowCopyId} for consistent capture.");
            }
            catch (Exception vssEx)
            {
                _log.Log($"VSS snapshot unavailable: {vssEx.Message}");
                if (!_dialog.ConfirmWarning(
                    LocExtension.Format("VssUnavailableBody", vssEx.Message),
                    LocExtension.Get("VssUnavailableTitle")))
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

                StatusText = LocExtension.Get("StatusVhdxCapturing");
                var safeFileName = ProcessRunner.EscapePowerShellString(Path.GetFileName(ImagePath));
                var letterCmd = $"(Get-Disk | Where-Object {{ $_.Location -like ('*' + {safeFileName} + '*') }} | Get-Partition | Where-Object {{ $_.DriveLetter }} | Select-Object -First 1).DriveLetter";
                var vhdLetter = (await _processRunner.RunPowerShellAsync(letterCmd, _log, ct)).Trim();
                var mountedLetter = RequireDriveLetter(vhdLetter, "mounted VHDX target");

                try
                {
                    await _processRunner.RunExeAsync("robocopy",
                        CloneWorkflowService.BuildVhdxCaptureArguments(captureSource, mountedLetter),
                        _log, ignoreStderrOnSuccess: true, ct: ct);
                }
                catch (Exception ex) when (CloneWorkflowService.IsMissingPrivilegeFailure(ex.Message))
                {
                    _log.Log("Robocopy refused auditing or backup-mode copies because this session lacks the " +
                             "required user rights. Retrying with ACLs and ownership only — audit entries will " +
                             "not be captured.");
                    await _processRunner.RunExeAsync("robocopy",
                        CloneWorkflowService.BuildVhdxCaptureArguments(captureSource, mountedLetter, privileged: false),
                        _log, ignoreStderrOnSuccess: true, ct: ct);
                }

                await _processRunner.RunDiskpartAsync(detachScript, _log, ct);
                detachCleanup.Complete();
            }

            StatusText = LocExtension.Get("StatusWritingManifest");
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
                vssCleanup?.Complete();
                _log.Log("VSS snapshot cleaned up after successful capture.");
            }

            if (EncryptImage)
            {
                StatusText = LocExtension.Get("StatusEncryptingImage");
                var password = PromptForInput(LocExtension.Get("EncryptPasswordPrompt"), LocExtension.Get("EncryptImageTitle"));
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
            _dialog.ShowInfo(
                LocExtension.Format("ImageCreatedBody", ImagePath),
                LocExtension.Get("ImageCreatedTitle"));
        }
        catch (OperationCanceledException)
        {
            _log.Log("Image creation cancelled.");
        }
        catch (Exception ex)
        {
            _log.Log($"Image creation failed: {ex.Message}");
            _dialog.ShowError(
                LocExtension.Format("ImageCreationFailed", ex.Message),
                LocExtension.Get("CreateImageErrorTitle"));
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
                    LocExtension.Format("OpRestoreImageToDisk", SelectedTargetDisk.Number),
                    protectedTargets),
                LocExtension.Get("BitLockerRestoreTitle")))
        {
            return;
        }

        if (!_dialog.ConfirmDanger(
            LocExtension.Format("RestoreDestroyWarning", targetIdentity.ConfirmationSummary),
            LocExtension.Get("ConfirmImageRestoreTitle"))) return;

        if (!_dialog.ConfirmDanger(
            LocExtension.Get("RestoreFinalConfirmation"),
            LocExtension.Get("ConfirmRestoreTitle"))) return;

        string restorePath = RestoreImagePath;
        string? tempDecrypted = null;
        var imageManifestValidation = await ValidateRestoreImageManifestOrConfirmAsync(restorePath, CancellationToken.None);
        if (imageManifestValidation is null)
            return;

        if (ImageEncryptionService.IsEncryptedImage(restorePath))
        {
            var password = PromptForInput(LocExtension.Get("DecryptPasswordPrompt"), LocExtension.Get("DecryptImageTitle"));
            if (string.IsNullOrEmpty(password))
            {
                _dialog.ShowWarning(
                    LocExtension.Get("RestoreCancelledNoPassword"),
                    LocExtension.Get("EncryptedImageTitle"));
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
                _dialog.ShowError(
                    LocExtension.Format("DecryptionFailed", ex.Message),
                    LocExtension.Get("DecryptErrorTitle"));
                return;
            }
        }

        if (!await DestructiveWorkflowGuard.VerifyDiskIdentityBeforeExecuteAsync(
                targetIdentity, "Restore Target Changed", _wmiService, _log, _dialog))
            return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsBusy = true;
        StatusText = LocExtension.Format("StatusRestoringImage", SelectedTargetDisk.Number);

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

            StatusText = LocExtension.Get("StatusSavingTargetSnapshot");
            await _backup.SaveSnapshotForDestructiveOperationAsync(diskNum, "image restore", ct);

            // Best-effort lock volumes on target disk before clearing
            var targetPartitions = await _wmiService.GetPartitionsAsync(diskNum);
            targetLocks = targetPartitions
                .Where(p => p.DriveLetter.HasValue)
                .Select(p => VolumeLockService.RequireLock(p.DriveLetter!.Value, _log))
                .ToList();

            StatusText = LocExtension.Get("StatusClearingTarget");
            var clearCmd = $"Clear-Disk -Number {diskNum} -RemoveData -RemoveOEM -Confirm:$false";
            await _processRunner.RunPowerShellAsync(clearCmd, _log, ct);

            var initCmd = $"Initialize-Disk -Number {diskNum} -PartitionStyle GPT -Confirm:$false";
            await _processRunner.RunPowerShellAsync(initCmd, _log, ct);

            if (ext == ".wim")
            {
                StatusText = LocExtension.Get("StatusApplyingWim");
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
                StatusText = LocExtension.Get("StatusMountingVhdx");
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

            StatusText = LocExtension.Get("StatusAuditingRestore");
            var bootAudit = await RunBootabilityAuditAsync(diskNum, restoredWindowsDrive, ct);
            var bootAuditReport = bootAudit.FormatReport();
            _log.Log($"Image restored to Disk {diskNum}.");
            _log.Log(bootAuditReport);

            var restoreSummary = LocExtension.Format("RestoreCompleteBody", diskNum, bootAuditReport);
            if (bootAudit.Status == BootabilityAuditStatus.Pass)
                _dialog.ShowInfo(restoreSummary, LocExtension.Get("RestoreCompleteTitle"));
            else
                _dialog.ShowWarning(restoreSummary, LocExtension.Get("RestoreCompleteWarningsTitle"));
        }
        catch (OperationCanceledException)
        {
            _log.Log("Image restore cancelled.");
        }
        catch (Exception ex)
        {
            _log.Log($"Image restore failed: {ex.Message}");
            _dialog.ShowError(
                LocExtension.Format("ImageRestoreFailed", ex.Message),
                LocExtension.Get("RestoreErrorTitle"));
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
            _dialog.ShowError(ex.Message, LocExtension.Get("CloneValidationFailedTitle"));
            return;
        }

        var protectedTargets = await GetBitLockerProtectedTargetsAsync(CloneDestDisk.Number);
        if (protectedTargets.Count > 0 &&
            !_dialog.ConfirmDanger(
                BitLockerPreflight.BuildDestructiveConfirmation(
                    LocExtension.Format("OpSectorCloneToDisk", CloneDestDisk.Number), protectedTargets),
                LocExtension.Get("BitLockerCloneTitle")))
            return;

        if (!DestructiveWorkflowGuard.ConfirmPrompts(
                CloneWorkflowService.BuildSectorClonePrompts(sourceIdentity, destIdentity), _dialog))
            return;

        if (!await DestructiveWorkflowGuard.VerifyDiskIdentityBeforeExecuteAsync(
                sourceIdentity, "Clone Source Changed", _wmiService, _log, _dialog))
            return;
        if (!await DestructiveWorkflowGuard.VerifyDiskIdentityBeforeExecuteAsync(
                destIdentity, "Clone Destination Changed", _wmiService, _log, _dialog))
            return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsBusy = true;
        StatusText = LocExtension.Format("StatusCloning", CloneSourceDisk.Number, CloneDestDisk.Number);
        CloneProgressText = LocExtension.Get("CloneStarting");
        CloneProgressPercent = 0;

        var targetLocks = new List<VolumeLock>();
        try
        {
            await sourceIdentity.VerifyCurrentAsync(_wmiService);
            await destIdentity.VerifyCurrentAsync(_wmiService);

            StatusText = LocExtension.Get("StatusSavingDestSnapshot");
            await _backup.SaveSnapshotForDestructiveOperationAsync(CloneDestDisk.Number, "sector clone", ct);

            var targetPartitions = await _wmiService.GetPartitionsAsync(CloneDestDisk.Number);
            targetLocks = targetPartitions
                .Where(p => p.DriveLetter.HasValue)
                .Select(p => VolumeLockService.RequireLock(p.DriveLetter!.Value, _log))
                .ToList();

            var progress = new Progress<SectorCloneProgress>(p =>
            {
                CloneProgressText = LocExtension.Format("CloneProgressLine",
                    p.ProgressText, p.RateText, p.EstimatedRemaining.ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture));
                CloneProgressPercent = p.PercentComplete;
                StatusText = LocExtension.Format("StatusCloningPercent",
                p.PercentComplete.ToString("F1", CultureInfo.CurrentCulture));
            });

            var cloneResult = await SectorCloneService.CloneAsync(
                CloneSourceDisk.Number, CloneDestDisk.Number, CloneSourceDisk.Size,
                CloneDestDisk.Size, CloneDestDisk.LogicalSectorSize,
                _log, progress, ct, rescue: CloneRescueMode, verify: CloneVerify);

            CloneProgressPercent = 100;
            StatusText = LocExtension.Get("StatusAuditingClone");
            var bootAudit = await RunBootabilityAuditAsync(CloneDestDisk.Number, null, ct);
            var bootAuditReport = bootAudit.FormatReport();
            _log.Log(bootAuditReport);
            CloneProgressText = $"{cloneResult.FormatReport()}\n\n{bootAuditReport}";

            var summary = CloneWorkflowService.BuildCompletionSummary(
                CloneSourceDisk.Number,
                CloneDestDisk.Number,
                cloneResult.FormatReport(),
                bootAuditReport);
            if (cloneResult.HasBadSectors || !cloneResult.VerificationPassed || bootAudit.Status != BootabilityAuditStatus.Pass)
                _dialog.ShowWarning(summary, LocExtension.Get("CloneCompleteWarningsTitle"));
            else
                _dialog.ShowInfo(summary, LocExtension.Get("CloneCompleteTitle"));
        }
        catch (OperationCanceledException)
        {
            _log.Log("Sector clone cancelled.");
            CloneProgressText = LocExtension.Get("CloneCancelled");
        }
        catch (Exception ex)
        {
            _log.Log($"Sector clone failed: {ex.Message}");
            _dialog.ShowError(
                LocExtension.Format("SectorCloneFailed", ex.Message),
                LocExtension.Get("CloneErrorTitle"));
            CloneProgressText = LocExtension.Format("CloneFailedShort", ex.Message);
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
            ImagePreflightSummary = LocExtension.Get("PreflightChooseSource");
            return;
        }

        if (string.IsNullOrWhiteSpace(ImagePath))
        {
            ImagePreflightSummary = LocExtension.Get("PreflightChooseDestination");
            return;
        }

        try
        {
            var preflight = PreflightSelectedImageDestination();
            ImagePreflightSummary = LocExtension.Format("PreflightEstimate",
                SizeUtil.Format(preflight.EstimatedRequiredBytes),
                SizeUtil.Format(preflight.DestinationFreeBytes));
        }
        catch (Exception ex)
        {
            ImagePreflightSummary = LocExtension.Format("PreflightDestinationCheck", ex.Message);
        }
    }

    private ImageDestinationPreflight PreflightSelectedImageDestination()
    {
        var requiredBytes = EstimateSelectedImageBytes();
        return DiskImageWorkflowService.PreflightDestination(
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
            ? DiskImageWorkflowService.EstimateImageBytes(volume.Size, volume.SizeRemaining)
            : 0;
    }

    private void GuardSourceVolumeForImageCapture(char sourceDrive)
    {
        DiskImageWorkflowService.GuardSourceVolumeForCapture(sourceDrive, _sourceBitLockerByLetter);
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
            LocExtension.Format("ManifestDegradedBody", validation.Detail),
            LocExtension.Get("ManifestVerificationTitle"))
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
            LocExtension.Format("DecryptedHashMismatchBody", manifest.PlainImageSha256, actual),
            LocExtension.Get("DecryptedImageVerificationTitle"));
    }

    private static string? PromptForInput(string message, string title)
    {
        var dialog = new Dialogs.PasswordPromptDialog(message, title)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true ? dialog.Password : null;
    }

}
