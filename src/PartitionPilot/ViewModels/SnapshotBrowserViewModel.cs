using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace PartitionPilot;

public class SnapshotBrowserViewModel : ViewModelBase
{
    private readonly PartitionTableBackup _backup;
    private readonly ActivityLog _log;
    private readonly IDialogService _dialog;
    private readonly IWmiDiskService _wmiService;
    private readonly IProcessRunner _processRunner;

    public ObservableCollection<PartitionSnapshot> Snapshots { get; } = new();
    public ObservableCollection<PartitionSnapshotPartition> SnapshotPartitions { get; } = new();

    private PartitionSnapshot? _selectedSnapshot;
    public PartitionSnapshot? SelectedSnapshot
    {
        get => _selectedSnapshot;
        set
        {
            if (SetProperty(ref _selectedSnapshot, value))
            {
                LoadSelectedSnapshot();
                CommandManager.InvalidateRequerySuggested();
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
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private string _summaryText = LocExtension.Get("SnapshotsRefreshPrompt");
    public string SummaryText
    {
        get => _summaryText;
        set => SetProperty(ref _summaryText, value);
    }

    private string _diffText = LocExtension.Get("SnapshotSelectPrompt");
    public string DiffText
    {
        get => _diffText;
        set => SetProperty(ref _diffText, value);
    }

    private string _recoveryCommands = "";
    public string RecoveryCommands
    {
        get => _recoveryCommands;
        set => SetProperty(ref _recoveryCommands, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand CompareCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ExportRecoveryPlanCommand { get; }
    public ICommand CopyRecoveryCommandsCommand { get; }
    public ICommand PreviewRestoreCommand { get; }
    public ICommand RestoreCommand { get; }

    public SnapshotBrowserViewModel(
        PartitionTableBackup backup,
        ActivityLog log,
        IDialogService dialog,
        IWmiDiskService wmiService,
        IProcessRunner processRunner)
    {
        _backup = backup;
        _log = log;
        _dialog = dialog;
        _wmiService = wmiService;
        _processRunner = processRunner;

        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        CompareCommand = new AsyncRelayCommand(_ => CompareAsync(), _ => SelectedSnapshot is not null);
        ExportCommand = new AsyncRelayCommand(_ => ExportSelectedAsync(), _ => SelectedSnapshot is not null);
        ExportRecoveryPlanCommand = new AsyncRelayCommand(_ => ExportRecoveryPlanAsync(), _ => SelectedSnapshot is not null);
        CopyRecoveryCommandsCommand = new WpfRelayCommand(_ => CopyRecoveryCommands(), _ => !string.IsNullOrWhiteSpace(RecoveryCommands));
        PreviewRestoreCommand = new AsyncRelayCommand(_ => PreviewRestoreAsync(), _ => SelectedSnapshot is not null && !IsBusy);
        RestoreCommand = new AsyncRelayCommand(_ => RestoreAsync(), _ => SelectedSnapshot is not null && !IsBusy);
    }

    /// <summary>
    /// Builds the restore plan for the selected snapshot and shows it without touching the disk.
    /// </summary>
    private async Task PreviewRestoreAsync()
    {
        var plan = await BuildRestorePlanAsync();
        if (plan is null)
            return;

        DiffText = plan.Value.Plan.FormatPlan();
        _log.Log($"Previewed restore of snapshot {SelectedSnapshot!.FileName} onto Disk {plan.Value.Disk.Number}.");
    }

    private async Task RestoreAsync()
    {
        var built = await BuildRestorePlanAsync();
        if (built is null)
            return;

        var (plan, disk) = built.Value;
        DiffText = plan.FormatPlan();

        var blocked = plan.Steps.Where(s => s.RiskLevel == "Blocked").ToList();
        if (blocked.Count > 0)
        {
            _dialog.ShowError(
                LocExtension.Format("RestoreCannotProceed", string.Join("\n", blocked.Select(b => b.Description))),
                LocExtension.Get("RestoreSnapshotTitle"));
            return;
        }

        var identity = disk.ToIdentitySnapshot();
        var notRecreated = plan.SkippedPartitions.Count == 0
            ? ""
            : LocExtension.Format("RestoreNotRecreatedHeading", string.Join("\n", plan.SkippedPartitions));

        var prompts = new[]
        {
            new WorkflowPrompt(
                LocExtension.Get("RestoreSnapshotTitle"),
                LocExtension.Format("RestoreWarningPrompt",
                    disk.Number, identity.ConfirmationSummary, plan.FormatPlan(), notRecreated),
                true),
            new WorkflowPrompt(
                LocExtension.Get("RestoreSnapshotTitle"),
                LocExtension.Format("RestoreFinalPrompt", disk.Number),
                true)
        };

        if (!DestructiveWorkflowGuard.ConfirmPrompts(prompts, _dialog))
        {
            _log.Log("Snapshot restore cancelled by user.");
            return;
        }

        IsBusy = true;
        try
        {
            var destructiveStepsRun = false;

            foreach (var step in plan.Steps)
            {
                if (!await DestructiveWorkflowGuard.VerifyDiskIdentityBeforeExecuteAsync(
                        identity, "Restore Snapshot", _wmiService, _log, _dialog))
                {
                    ReportInterruptedRestore(disk.Number, destructiveStepsRun);
                    return;
                }

                if (string.IsNullOrEmpty(step.DiskpartScript))
                {
                    _log.Log($"Skipping (no automated script): {step.Description}");
                    continue;
                }

                _log.Log($"Restore step: {step.Description}");
                await _processRunner.RunDiskpartAsync(step.DiskpartScript, _log);

                if (step.RiskLevel == "Destructive")
                    destructiveStepsRun = true;
            }

            _log.Log($"Snapshot {SelectedSnapshot!.FileName} restored onto Disk {disk.Number}.");
            _dialog.ShowInfo(
                LocExtension.Format("RestoreComplete", disk.Number, notRecreated),
                LocExtension.Get("RestoreSnapshotTitle"));
        }
        catch (Exception ex)
        {
            _log.Log($"Snapshot restore failed: {ex.Message}");
            _dialog.ShowError(
                LocExtension.Format("RestoreFailedPartway", ex.Message, disk.Number),
                LocExtension.Get("RestoreSnapshotTitle"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// A restore that stops after the disk has been cleared leaves it with no partition table. Saying so
    /// plainly matters more than the generic guard message the operator would otherwise be left with.
    /// </summary>
    private void ReportInterruptedRestore(int diskNumber, bool destructiveStepsRun)
    {
        if (!destructiveStepsRun)
        {
            _log.Log($"Snapshot restore stopped before any change was made to Disk {diskNumber}.");
            return;
        }

        _log.Log(
            $"Restore stopped after Disk {diskNumber} was already cleared, so it currently has no usable " +
            "partition table. Re-run the restore once the identity check passes; do not power off in between.");
        _dialog.ShowError(
            LocExtension.Format("RestoreStoppedAfterClear", diskNumber),
            LocExtension.Get("RestoreSnapshotTitle"));
    }

    private async Task<(SnapshotRestorePlan Plan, DiskInfo Disk)?> BuildRestorePlanAsync()
    {
        if (SelectedSnapshot is null)
            return null;

        IsBusy = true;
        try
        {
            var disks = await _wmiService.GetDisksAsync();
            var disk = disks.FirstOrDefault(d => d.Number == SelectedSnapshot.DiskNumber);
            if (disk is null)
            {
                _dialog.ShowError(
                    LocExtension.Format("DiskNotConnected", SelectedSnapshot.DiskNumber),
                    LocExtension.Get("RestoreSnapshotTitle"));
                return null;
            }

            var currentPartitions = await _wmiService.GetPartitionsAsync(disk.Number);
            return (SnapshotRestoreService.BuildPlan(SelectedSnapshot, disk, currentPartitions), disk);
        }
        catch (Exception ex)
        {
            _log.Log($"Restore plan failed: {ex.Message}");
            _dialog.ShowError(
                LocExtension.Format("RestoreBlocked", ex.Message),
                LocExtension.Get("RestoreSnapshotTitle"));
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var snapshots = await _backup.ListSnapshotsAsync();

            Application.Current.Dispatcher.Invoke(() =>
            {
                Snapshots.Clear();
                foreach (var snapshot in snapshots)
                    Snapshots.Add(snapshot);

                SelectedSnapshot = Snapshots.FirstOrDefault();
            });
            SummaryText = snapshots.Count == 0
                ? LocExtension.Format("SnapshotsNoneFound", PartitionTableBackup.BackupDirectory)
                : LocExtension.Format("SnapshotsLoaded", snapshots.Count, PartitionTableBackup.BackupDirectory);

            _log.Log($"Loaded {snapshots.Count} partition snapshot(s).");
        }
        catch (Exception ex)
        {
            _log.Log($"Snapshot refresh failed: {ex.Message}");
            _dialog.ShowError(
                LocExtension.Format("SnapshotLoadFailed", ex.Message),
                LocExtension.Get("SnapshotErrorTitle"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CompareAsync()
    {
        if (SelectedSnapshot is null) return;

        IsBusy = true;
        try
        {
            DiffText = await _backup.CompareSnapshotToCurrentAsync(SelectedSnapshot);
            _log.Log($"Compared snapshot {SelectedSnapshot.FileName} with current Disk {SelectedSnapshot.DiskNumber} layout.");
        }
        catch (Exception ex)
        {
            _log.Log($"Snapshot compare failed: {ex.Message}");
            _dialog.ShowError(
                LocExtension.Format("SnapshotCompareFailed", ex.Message),
                LocExtension.Get("CompareSnapshotTitle"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportSelectedAsync()
    {
        if (SelectedSnapshot is null) return;

        var dialog = new SaveFileDialog
        {
            Title = LocExtension.Get("ExportPartitionSnapshotTitle"),
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            FileName = SelectedSnapshot.FileName,
            DefaultExt = ".json",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await _backup.ExportSnapshotAsync(SelectedSnapshot, dialog.FileName);
            _log.Log($"Snapshot exported to: {dialog.FileName}");
            _dialog.ShowInfo(
                LocExtension.Format("SnapshotExported", dialog.FileName),
                LocExtension.Get("SnapshotExportedTitle"));
        }
        catch (Exception ex)
        {
            _log.Log($"Snapshot export failed: {ex.Message}");
            _dialog.ShowError(
                LocExtension.Format("SnapshotExportFailed", ex.Message),
                LocExtension.Get("ExportSnapshotTitle"));
        }
    }

    private async Task ExportRecoveryPlanAsync()
    {
        if (SelectedSnapshot is null) return;

        var dialog = new SaveFileDialog
        {
            Title = LocExtension.Get("ExportRecoveryPlanTitle"),
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            FileName = $"recovery-plan-disk{SelectedSnapshot.DiskNumber}_{DateTime.Now:yyyyMMdd}.txt",
            DefaultExt = ".txt"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var plan = await _backup.BuildRecoveryPlanAsync(SelectedSnapshot);
            await System.IO.File.WriteAllTextAsync(dialog.FileName, plan);
            _log.Log($"Recovery plan exported to: {dialog.FileName}");
            _dialog.ShowInfo(
                LocExtension.Format("RecoveryPlanExported", dialog.FileName),
                LocExtension.Get("RecoveryPlanExportedTitle"));
        }
        catch (Exception ex)
        {
            _log.Log($"Recovery plan export failed: {ex.Message}");
            _dialog.ShowError(
                LocExtension.Format("RecoveryPlanExportFailed", ex.Message),
                LocExtension.Get("ExportErrorTitle"));
        }
    }

    private void CopyRecoveryCommands()
    {
        if (string.IsNullOrWhiteSpace(RecoveryCommands)) return;

        Clipboard.SetText(RecoveryCommands);
        _log.Log("Copied snapshot recovery guidance to clipboard.");
        _dialog.ShowInfo(
            LocExtension.Get("RecoveryGuidanceCopied"),
            LocExtension.Get("RecoveryGuidanceTitle"));
    }

    private void LoadSelectedSnapshot()
    {
        SnapshotPartitions.Clear();

        if (SelectedSnapshot is null)
        {
            DiffText = LocExtension.Get("SnapshotSelectPrompt");
            RecoveryCommands = "";
            return;
        }

        foreach (var partition in SelectedSnapshot.Partitions.OrderBy(p => p.PartitionNumber))
            SnapshotPartitions.Add(partition);

        DiffText = LocExtension.Format("SnapshotSelectedHint", SelectedSnapshot.FileName);
        RecoveryCommands = PartitionTableBackup.BuildRecoveryCommands(SelectedSnapshot);
    }
}
