using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace PartitionPilot;

public class PartitionsViewModel : ViewModelBase
{
    private readonly WmiDiskService _wmiService;
    private readonly ProcessRunner _processRunner;
    private readonly ActivityLog _log;
    private readonly IDialogService _dialog;
    private readonly PartitionTableBackup _backup;
    private CancellationTokenSource? _loadCts;

    public ObservableCollection<DiskInfo> Disks { get; } = new();
    public ObservableCollection<PartitionInfo> Partitions { get; } = new();
    public ObservableCollection<DiskBarSegment> DiskBarSegments { get; } = new();

    private DiskInfo? _selectedDisk;
    public DiskInfo? SelectedDisk
    {
        get => _selectedDisk;
        set
        {
            if (SetProperty(ref _selectedDisk, value))
            {
                OnPropertyChanged(nameof(HasSelectedDisk));
                OnPropertyChanged(nameof(SelectedDiskSummary));
                OnPropertyChanged(nameof(DiskCapacityText));
                OnPropertyChanged(nameof(DiskFreeExtentText));
                OnPropertyChanged(nameof(DiskPartitionStyleText));
                CommandManager.InvalidateRequerySuggested();
                _loadCts?.Cancel();
                _loadCts?.Dispose();
                _loadCts = new CancellationTokenSource();
                _ = LoadPartitionsAsync(_loadCts.Token);
            }
        }
    }

    private PartitionInfo? _selectedPartition;
    public PartitionInfo? SelectedPartition
    {
        get => _selectedPartition;
        set
        {
            if (SetProperty(ref _selectedPartition, value))
            {
                OnPropertyChanged(nameof(HasSelectedPartition));
                OnPropertyChanged(nameof(SelectedPartitionName));
                OnPropertyChanged(nameof(SelectedPartitionSummary));
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

    private string? _pendingOperation;
    public string? PendingOperation
    {
        get => _pendingOperation;
        set => SetProperty(ref _pendingOperation, value);
    }

    public bool HasSelectedDisk => SelectedDisk is not null;

    public bool HasSelectedPartition => SelectedPartition is not null;

    public string SelectedDiskSummary => SelectedDisk is null
        ? "Select a physical disk to review capacity, layout, and available partition operations."
        : $"{SizeUtil.Format(SelectedDisk.Size)} total | {SelectedDisk.PartitionStyle} | {SelectedDisk.NumberOfPartitions} partitions | {SizeUtil.Format(SelectedDisk.LargestFreeExtent)} largest free extent";

    public string DiskCapacityText => SelectedDisk is null ? "No disk selected" : $"{SizeUtil.Format(SelectedDisk.Size)} total";

    public string DiskFreeExtentText => SelectedDisk is null
        ? "Refresh disks to load free-space data"
        : $"{SizeUtil.Format(SelectedDisk.LargestFreeExtent)} largest free extent";

    public string DiskPartitionStyleText => SelectedDisk is null ? "Partition style unavailable" : $"{SelectedDisk.PartitionStyle} partition style";

    public string SelectedPartitionName => SelectedPartition is null ? "No partition selected" : SelectedPartition.PartitionDisplay;

    public string SelectedPartitionSummary
    {
        get
        {
            if (SelectedPartition is null)
                return "Select a table row or disk-map segment before applying partition actions.";

            var fileSystem = string.IsNullOrWhiteSpace(SelectedPartition.FileSystem) ? "No file system" : SelectedPartition.FileSystem;
            return $"{SelectedPartition.SizeText} | {fileSystem} | {SelectedPartition.Details}";
        }
    }

    // Commands
    public ICommand RefreshCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ExtendCommand { get; }
    public ICommand SetActiveCommand { get; }
    public ICommand HideCommand { get; }

    // Color map for disk bar segments
    private static readonly Dictionary<string, string> SegmentColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["System"]      = "#4CC2FF",
        ["Reserved"]    = "#8391A2",
        ["Recovery"]    = "#F4C96A",
        ["Basic"]       = "#5EE0A0",
        ["Unallocated"] = "#343A42",
    };

    private const string DefaultColor = "#B18CFF";
    private const double MinProportion = 0.018;

    public PartitionsViewModel(WmiDiskService wmiService, ProcessRunner processRunner, ActivityLog log, IDialogService dialog)
    {
        _wmiService = wmiService;
        _processRunner = processRunner;
        _log = log;
        _dialog = dialog;
        _backup = new PartitionTableBackup(wmiService, log);

        RefreshCommand = new AsyncRelayCommand(_ => LoadDisksAsync());
        DeleteCommand = new AsyncRelayCommand(_ => ExecuteDeleteAsync(), _ => SelectedPartition is not null);
        ExtendCommand = new AsyncRelayCommand(_ => ExecuteExtendAsync(), _ => SelectedPartition is not null);
        SetActiveCommand = new AsyncRelayCommand(_ => ExecuteSetActiveAsync(), _ => SelectedPartition is not null);
        HideCommand = new AsyncRelayCommand(_ => ExecuteHideToggleAsync(), _ => SelectedPartition is not null);
    }

    // ──────────────────────── Delegate Methods ────────────────────────

    public Task<List<char>> GetAvailableLettersAsync() => _wmiService.GetAvailableLettersAsync();

    public Task<(long Min, long Max)> GetSupportedSizeAsync(char letter) => _wmiService.GetPartitionSupportedSizeAsync(letter);

    // ──────────────────────── Load Methods ────────────────────────

    public async Task LoadDisksAsync()
    {
        IsBusy = true;
        try
        {
            _log.Log("Refreshing disk list...");
            var priorDiskNumber = SelectedDisk?.Number;
            var disks = await _wmiService.GetDisksAsync();

            Application.Current.Dispatcher.Invoke(() =>
            {
                Disks.Clear();
                foreach (var d in disks)
                    Disks.Add(d);

                SelectedDisk = Disks.FirstOrDefault(d => d.Number == priorDiskNumber) ?? Disks.FirstOrDefault();
            });

            _log.Log($"Found {disks.Count} disk(s).");
        }
        catch (Exception ex)
        {
            _log.Log($"Error loading disks: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task LoadPartitionsAsync() => LoadPartitionsAsync(CancellationToken.None);

    private async Task LoadPartitionsAsync(CancellationToken ct)
    {
        if (SelectedDisk is null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Partitions.Clear();
                DiskBarSegments.Clear();
            });
            return;
        }

        IsBusy = true;
        try
        {
            var disk = SelectedDisk;
            _log.Log($"Loading partitions for Disk {disk.Number}...");

            var parts = await _wmiService.GetPartitionsAsync(disk.Number);
            ct.ThrowIfCancellationRequested();
            var vols = await _wmiService.GetVolumesAsync();
            ct.ThrowIfCancellationRequested();
            WmiDiskService.EnrichPartitionsWithVolumes(parts, vols);

            var pagefileLetters = await _wmiService.GetPagefileLocationsAsync();
            var bitlockerStatus = await _wmiService.GetBitLockerStatusAsync();
            ct.ThrowIfCancellationRequested();
            foreach (var p in parts)
            {
                if (p.DriveLetter.HasValue)
                {
                    if (pagefileLetters.Contains(p.DriveLetter.Value))
                        p.HasPagefile = true;
                    if (bitlockerStatus.TryGetValue(p.DriveLetter.Value, out var blStatus))
                        p.EncryptionStatus = blStatus;
                }
            }

            ct.ThrowIfCancellationRequested();
            Application.Current.Dispatcher.Invoke(() =>
            {
                Partitions.Clear();
                foreach (var p in parts)
                    Partitions.Add(p);
            });

            ComputeDiskBarSegments(disk, parts);
            _log.Log($"Loaded {parts.Count} partition(s) for Disk {disk.Number}.");
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load request — expected, don't log as error
        }
        catch (Exception ex)
        {
            _log.Log($"Error loading partitions: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ──────────────────────── Disk Bar ────────────────────────

    private void ComputeDiskBarSegments(DiskInfo disk, List<PartitionInfo> partitions)
    {
        var segments = new List<DiskBarSegment>();
        long totalSize = disk.Size;
        if (totalSize <= 0) return;

        // Sort by offset
        var sorted = partitions.OrderBy(p => p.Offset).ToList();

        long cursor = 0;
        foreach (var part in sorted)
        {
            // Gap before this partition = unallocated
            if (part.Offset > cursor)
            {
                long gap = part.Offset - cursor;
                segments.Add(new DiskBarSegment
                {
                    Type = "Unallocated",
                    SizeBytes = gap,
                    Label = "Unallocated",
                    ColorHex = SegmentColors["Unallocated"],
                });
            }

            string label = part.DriveLetter.HasValue
                ? $"{part.DriveLetter}: {(!string.IsNullOrWhiteSpace(part.Label) ? part.Label : "Volume")}"
                : part.PartitionDisplay;

            segments.Add(new DiskBarSegment
            {
                Type = part.Type,
                SizeBytes = part.Size,
                Label = label,
                ColorHex = SegmentColors.GetValueOrDefault(part.Type, DefaultColor),
            });

            cursor = part.Offset + part.Size;
        }

        // Trailing unallocated space
        if (cursor < totalSize)
        {
            long gap = totalSize - cursor;
                segments.Add(new DiskBarSegment
                {
                    Type = "Unallocated",
                    SizeBytes = gap,
                    Label = "Unallocated",
                    ColorHex = SegmentColors["Unallocated"],
                });
        }

        // Compute proportions, enforce minimum
        foreach (var seg in segments)
            seg.Proportion = (double)seg.SizeBytes / totalSize;

        // Enforce minimum proportion
        double totalBorrowed = 0;
        int belowMinCount = 0;
        foreach (var seg in segments)
        {
            if (seg.Proportion < MinProportion)
            {
                totalBorrowed += MinProportion - seg.Proportion;
                seg.Proportion = MinProportion;
                belowMinCount++;
            }
        }

        if (totalBorrowed > 0 && segments.Count > belowMinCount)
        {
            // Redistribute borrowed space from segments above minimum
            var aboveMin = segments.Where(s => s.Proportion > MinProportion).ToList();
            double totalAbove = aboveMin.Sum(s => s.Proportion);
            foreach (var seg in aboveMin)
                seg.Proportion -= totalBorrowed * (seg.Proportion / totalAbove);
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            DiskBarSegments.Clear();
            foreach (var seg in segments)
                DiskBarSegments.Add(seg);
        });
    }

    // ──────────────────────── Dialog-driven Operations ────────────────────────

    public async Task ExecuteCreateAsync(double sizeGB, char letter, string fs, string label, bool quick)
    {
        if (SelectedDisk is null) return;

        IsBusy = true;
        try
        {
            int diskNum = SelectedDisk.Number;
            long sizeMB = (long)(sizeGB * 1024);
            letter = ProcessRunner.ValidateDriveLetter(letter);
            label = ProcessRunner.SanitizeLabel(label);
            fs = ProcessRunner.ValidateFileSystem(fs);
            _log.Log($"Creating partition on Disk {diskNum}: {sizeGB:F2} GB, {fs}, letter={letter}...");

            string script = $"""
                select disk {diskNum}
                create partition primary size={sizeMB}
                assign letter={letter}
                format fs={fs} label="{label}" {(quick ? "quick" : "")}
                """;

            var result = await _processRunner.RunDiskpartAsync(script, _log);
            _log.Log($"Create result: {result.Trim()}");
            await LoadPartitionsAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"Create failed: {ex.Message}");
            _dialog.ShowError($"Failed to create partition:\n{ex.Message}", "Create Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExecuteFormatAsync(char letter, string fs, string label, bool quick, string? allocationUnitSize = null)
    {
        IsBusy = true;
        try
        {
            letter = ProcessRunner.ValidateDriveLetter(letter);
            label = ProcessRunner.SanitizeLabel(label);
            fs = ProcessRunner.ValidateFileSystem(fs);
            _log.Log($"Formatting {letter}: as {fs} (label=\"{label}\", quick={quick})...");

            if (SelectedDisk is not null)
                await _backup.SaveSnapshotAsync(SelectedDisk.Number);
            using var volumeLock = VolumeLockService.TryLock(letter, _log);

            var unitParam = !string.IsNullOrEmpty(allocationUnitSize) ? $"unit={allocationUnitSize} " : "";
            string script = $"""
                select volume {letter}
                format fs={fs} label="{label}" {unitParam}{(quick ? "quick" : "")}
                """;

            var result = await _processRunner.RunDiskpartAsync(script, _log);
            _log.Log($"Format result: {result.Trim()}");
            await LoadPartitionsAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"Format failed: {ex.Message}");
            _dialog.ShowError($"Failed to format volume:\n{ex.Message}", "Format Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExecuteResizeAsync(char letter, long newSizeBytes)
    {
        IsBusy = true;
        try
        {
            letter = ProcessRunner.ValidateDriveLetter(letter);
            _log.Log($"Resizing {letter}: to {SizeUtil.Format(newSizeBytes)}...");

            var cmd = $"Resize-Partition -DriveLetter '{letter}' -Size {newSizeBytes}";
            var result = await _processRunner.RunPowerShellAsync(cmd, _log);
            _log.Log($"Resize result: {result.Trim()}");
            await LoadPartitionsAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"Resize failed: {ex.Message}");
            _dialog.ShowError($"Failed to resize partition:\n{ex.Message}", "Resize Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExecuteSplitAsync(char letter, double newPartGB, char newLetter, string fs, string label)
    {
        IsBusy = true;
        try
        {
            letter = ProcessRunner.ValidateDriveLetter(letter);
            newLetter = ProcessRunner.ValidateDriveLetter(newLetter);
            label = ProcessRunner.SanitizeLabel(label);
            fs = ProcessRunner.ValidateFileSystem(fs);
            if (SelectedDisk is not null)
                await _backup.SaveSnapshotAsync(SelectedDisk.Number);
            _log.Log($"Splitting {letter}: shrink by {newPartGB:F2} GB, new partition {newLetter}:...");

            long shrinkMB = (long)(newPartGB * 1024);

            // Step 1: Shrink existing partition
            var shrinkCmd = $"Resize-Partition -DriveLetter '{letter}' -Size ((Get-Partition -DriveLetter '{letter}').Size - {shrinkMB * 1024 * 1024})";
            await _processRunner.RunPowerShellAsync(shrinkCmd, _log);

            // Step 2: Create new partition in the freed space
            if (SelectedDisk is null) return;
            string script = $"""
                select disk {SelectedDisk.Number}
                create partition primary size={shrinkMB}
                assign letter={newLetter}
                format fs={fs} label="{label}" quick
                """;

            var result = await _processRunner.RunDiskpartAsync(script, _log);
            _log.Log($"Split result: {result.Trim()}");
            await LoadPartitionsAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"Split failed: {ex.Message}");
            _dialog.ShowError($"Failed to split partition:\n{ex.Message}", "Split Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExecuteChangeLetterAsync(int partNum, char newLetter)
    {
        if (SelectedDisk is null) return;

        IsBusy = true;
        try
        {
            _log.Log($"Changing drive letter for Disk {SelectedDisk.Number}, Partition {partNum} to {newLetter}:...");

            var partition = Partitions.FirstOrDefault(p => p.PartitionNumber == partNum);

            string script;
            if (partition?.DriveLetter.HasValue == true)
            {
                script = $"""
                    select volume {partition.DriveLetter}
                    remove letter={partition.DriveLetter}
                    assign letter={newLetter}
                    """;
            }
            else
            {
                script = $"""
                    select disk {SelectedDisk.Number}
                    select partition {partNum}
                    assign letter={newLetter}
                    """;
            }

            var result = await _processRunner.RunDiskpartAsync(script, _log);
            _log.Log($"Change letter result: {result.Trim()}");
            await LoadPartitionsAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"Change letter failed: {ex.Message}");
            _dialog.ShowError($"Failed to change drive letter:\n{ex.Message}", "Change Letter Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ──────────────────────── Inline-confirmation Operations ────────────────────────

    private async Task ExecuteDeleteAsync()
    {
        if (SelectedPartition is null || SelectedDisk is null) return;

        var part = SelectedPartition;

        if (part.IsCritical)
        {
            if (!_dialog.ConfirmDanger(
                $"CRITICAL: Partition {part.PartitionNumber} is a {part.Type} partition" +
                (part.IsBoot ? " (Boot)" : "") + (part.IsSystem ? " (System)" : "") +
                $".\n\nDeleting it may make the system unbootable.\n\nDisk: {SelectedDisk.Number}, Letter: {part.LetterDisplay}, Size: {part.SizeText}\n\n" +
                "Type YES to confirm this destructive action.",
                "Delete Critical Partition")) return;
        }

        if (!_dialog.ConfirmWarning(
            $"Delete partition {part.PartitionNumber} on Disk {SelectedDisk.Number}?\n" +
            $"Letter: {part.LetterDisplay}, Size: {part.SizeText}\n\n" +
            "ALL DATA ON THIS PARTITION WILL BE LOST.",
            "Confirm Delete")) return;

        IsBusy = true;
        try
        {
            await _backup.SaveSnapshotAsync(SelectedDisk.Number);
            _log.Log($"Deleting partition {part.PartitionNumber} on Disk {SelectedDisk.Number}...");

            using var volumeLock = part.DriveLetter.HasValue
                ? VolumeLockService.TryLock(part.DriveLetter.Value, _log)
                : null;

            string script = $"""
                select disk {SelectedDisk.Number}
                select partition {part.PartitionNumber}
                delete partition override
                """;

            var output = await _processRunner.RunDiskpartAsync(script, _log);
            _log.Log($"Delete result: {output.Trim()}");
            SelectedPartition = null;
            await LoadPartitionsAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"Delete failed: {ex.Message}");
            _dialog.ShowError($"Failed to delete partition:\n{ex.Message}", "Delete Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteExtendAsync()
    {
        if (SelectedPartition is null || SelectedDisk is null) return;

        var part = SelectedPartition;

        // Warn about recovery / pagefile / system partitions
        var warnings = new List<string>();
        if (part.Type.Equals("Recovery", StringComparison.OrdinalIgnoreCase))
            warnings.Add("This is a Recovery partition. The recovery environment may need to be moved first.");
        if (part.HasPagefile)
            warnings.Add("This partition contains the Windows pagefile. You may need to temporarily disable it.");
        if (part.IsSystem)
            warnings.Add("This is a System partition.");

        string msg = $"Extend partition {part.PartitionNumber} ({part.LetterDisplay}) on Disk {SelectedDisk.Number} " +
                     "to fill all adjacent unallocated space?";
        if (warnings.Count > 0)
            msg += "\n\nWarnings:\n" + string.Join("\n", warnings.Select(w => $"  - {w}"));

        if (!_dialog.Confirm(msg, "Confirm Extend")) return;

        IsBusy = true;
        try
        {
            await _backup.SaveSnapshotAsync(SelectedDisk.Number);
            _log.Log($"Extending partition {part.PartitionNumber} on Disk {SelectedDisk.Number}...");

            if (part.Type.Equals("Recovery", StringComparison.OrdinalIgnoreCase))
            {
                // For recovery partitions, we need a more complex sequence:
                // 1. Delete the recovery partition
                // 2. Extend the previous partition
                // 3. Recreate recovery (user handles this)
                _log.Log("Recovery partition detected. Removing recovery env attributes...");

                var reagentOff = await _processRunner.RunExeAsync("reagentc", "/disable", _log);
                _log.Log($"reagentc /disable: {reagentOff.Trim()}");

                string deleteScript = $"""
                    select disk {SelectedDisk.Number}
                    select partition {part.PartitionNumber}
                    delete partition override
                    """;
                await _processRunner.RunDiskpartAsync(deleteScript, _log);
                _log.Log("Recovery partition deleted.");

                // Now find the partition right before it and extend
                var prevPart = Partitions
                    .Where(p => p.PartitionNumber < part.PartitionNumber && p.DriveLetter.HasValue)
                    .OrderByDescending(p => p.PartitionNumber)
                    .FirstOrDefault();

                if (prevPart?.DriveLetter is not null)
                {
                    var extendCmd = $"Resize-Partition -DriveLetter '{prevPart.DriveLetter}' " +
                                    $"-Size (Get-PartitionSupportedSize -DriveLetter '{prevPart.DriveLetter}').SizeMax";
                    await _processRunner.RunPowerShellAsync(extendCmd, _log);
                    _log.Log($"Extended {prevPart.DriveLetter}: to maximum size.");
                }
            }
            else if (part.HasPagefile)
            {
                _log.Log("Pagefile detected on partition. Attempting extend with pagefile awareness...");

                // Use diskpart extend which may work even with pagefile
                string script = $"""
                    select disk {SelectedDisk.Number}
                    select partition {part.PartitionNumber}
                    extend
                    """;
                var output = await _processRunner.RunDiskpartAsync(script, _log);
                _log.Log($"Extend result: {output.Trim()}");
            }
            else if (part.DriveLetter.HasValue)
            {
                // Standard extend via PowerShell
                var sizeInfo = await _wmiService.GetPartitionSupportedSizeAsync(part.DriveLetter.Value);
                var extendCmd = $"Resize-Partition -DriveLetter '{part.DriveLetter}' -Size {sizeInfo.Max}";
                await _processRunner.RunPowerShellAsync(extendCmd, _log);
                _log.Log($"Extended {part.DriveLetter}: to {SizeUtil.Format(sizeInfo.Max)}.");
            }
            else
            {
                // No drive letter, use diskpart.
                string script = $"""
                    select disk {SelectedDisk.Number}
                    select partition {part.PartitionNumber}
                    extend
                    """;
                var output = await _processRunner.RunDiskpartAsync(script, _log);
                _log.Log($"Extend result: {output.Trim()}");
            }

            await LoadPartitionsAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"Extend failed: {ex.Message}");
            _dialog.ShowError($"Failed to extend partition:\n{ex.Message}", "Extend Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteSetActiveAsync()
    {
        if (SelectedPartition is null || SelectedDisk is null) return;

        var part = SelectedPartition;
        if (!_dialog.ConfirmWarning(
            $"Set partition {part.PartitionNumber} on Disk {SelectedDisk.Number} as ACTIVE?\n\n" +
            "Warning: Setting the wrong partition as active can prevent Windows from booting.",
            "Confirm Set Active")) return;

        IsBusy = true;
        try
        {
            _log.Log($"Setting partition {part.PartitionNumber} on Disk {SelectedDisk.Number} as active...");

            var cmd = $"Set-Partition -DiskNumber {SelectedDisk.Number} -PartitionNumber {part.PartitionNumber} -IsActive $true";
            var result = await _processRunner.RunPowerShellAsync(cmd, _log);
            _log.Log($"Set active result: {result.Trim()}");
            await LoadPartitionsAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"Set active failed: {ex.Message}");
            _dialog.ShowError($"Failed to set partition as active:\n{ex.Message}", "Set Active Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteHideToggleAsync()
    {
        if (SelectedPartition is null || SelectedDisk is null) return;

        var part = SelectedPartition;
        bool willHide = !part.IsHidden;
        string action = willHide ? "Hide" : "Unhide";

        if (!_dialog.Confirm(
            $"{action} partition {part.PartitionNumber} ({part.LetterDisplay}) on Disk {SelectedDisk.Number}?",
            $"Confirm {action}")) return;

        IsBusy = true;
        try
        {
            _log.Log($"{action} partition {part.PartitionNumber} on Disk {SelectedDisk.Number}...");

            var cmd = $"Set-Partition -DiskNumber {SelectedDisk.Number} -PartitionNumber {part.PartitionNumber} -IsHidden ${willHide.ToString().ToLower()}";
            var result = await _processRunner.RunPowerShellAsync(cmd, _log);
            _log.Log($"{action} result: {result.Trim()}");
            await LoadPartitionsAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"{action} failed: {ex.Message}");
            _dialog.ShowError($"Failed to {action.ToLower()} partition:\n{ex.Message}", $"{action} Error");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
