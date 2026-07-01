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

    private string _summaryText = "Refresh to load saved partition table snapshots.";
    public string SummaryText
    {
        get => _summaryText;
        set => SetProperty(ref _summaryText, value);
    }

    private string _diffText = "Select a snapshot and compare it with the current disk layout.";
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

    public SnapshotBrowserViewModel(PartitionTableBackup backup, ActivityLog log, IDialogService dialog)
    {
        _backup = backup;
        _log = log;
        _dialog = dialog;

        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        CompareCommand = new AsyncRelayCommand(_ => CompareAsync(), _ => SelectedSnapshot is not null);
        ExportCommand = new AsyncRelayCommand(_ => ExportSelectedAsync(), _ => SelectedSnapshot is not null);
        ExportRecoveryPlanCommand = new AsyncRelayCommand(_ => ExportRecoveryPlanAsync(), _ => SelectedSnapshot is not null);
        CopyRecoveryCommandsCommand = new WpfRelayCommand(_ => CopyRecoveryCommands(), _ => !string.IsNullOrWhiteSpace(RecoveryCommands));
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
                ? $"No snapshots found in {PartitionTableBackup.BackupDirectory}."
                : $"{snapshots.Count} snapshot(s) loaded from {PartitionTableBackup.BackupDirectory}.";

            _log.Log($"Loaded {snapshots.Count} partition snapshot(s).");
        }
        catch (Exception ex)
        {
            _log.Log($"Snapshot refresh failed: {ex.Message}");
            _dialog.ShowError($"Failed to load snapshots:\n{ex.Message}", "Snapshot Error");
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
            _dialog.ShowError($"Failed to compare snapshot:\n{ex.Message}", "Compare Snapshot");
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
            Title = "Export Partition Snapshot",
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
            _dialog.ShowInfo($"Snapshot exported to:\n{dialog.FileName}", "Snapshot Exported");
        }
        catch (Exception ex)
        {
            _log.Log($"Snapshot export failed: {ex.Message}");
            _dialog.ShowError($"Failed to export snapshot:\n{ex.Message}", "Export Snapshot");
        }
    }

    private async Task ExportRecoveryPlanAsync()
    {
        if (SelectedSnapshot is null) return;

        var dialog = new SaveFileDialog
        {
            Title = "Export Recovery Plan",
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
            _dialog.ShowInfo($"Recovery plan exported to:\n{dialog.FileName}", "Recovery Plan Exported");
        }
        catch (Exception ex)
        {
            _log.Log($"Recovery plan export failed: {ex.Message}");
            _dialog.ShowError($"Failed to export recovery plan:\n{ex.Message}", "Export Error");
        }
    }

    private void CopyRecoveryCommands()
    {
        if (string.IsNullOrWhiteSpace(RecoveryCommands)) return;

        Clipboard.SetText(RecoveryCommands);
        _log.Log("Copied snapshot recovery guidance to clipboard.");
        _dialog.ShowInfo("Recovery guidance copied to the clipboard.", "Recovery Guidance");
    }

    private void LoadSelectedSnapshot()
    {
        SnapshotPartitions.Clear();

        if (SelectedSnapshot is null)
        {
            DiffText = "Select a snapshot and compare it with the current disk layout.";
            RecoveryCommands = "";
            return;
        }

        foreach (var partition in SelectedSnapshot.Partitions.OrderBy(p => p.PartitionNumber))
            SnapshotPartitions.Add(partition);

        DiffText = $"Selected {SelectedSnapshot.FileName}. Click Compare Current Layout to inspect drift.";
        RecoveryCommands = PartitionTableBackup.BuildRecoveryCommands(SelectedSnapshot);
    }
}
