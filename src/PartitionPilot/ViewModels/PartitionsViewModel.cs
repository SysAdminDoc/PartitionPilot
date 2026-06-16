using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace PartitionPilot;

public class PartitionsViewModel : ViewModelBase
{
    private readonly WmiDiskService _wmiService;
    private readonly ProcessRunner _processRunner;
    private readonly ActivityLog _log;

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
                CommandManager.InvalidateRequerySuggested();
                _ = LoadPartitionsAsync();
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

    public PartitionsViewModel(WmiDiskService wmiService, ProcessRunner processRunner, ActivityLog log)
    {
        _wmiService = wmiService;
        _processRunner = processRunner;
        _log = log;

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
            var disks = await _wmiService.GetDisksAsync();

            Application.Current.Dispatcher.Invoke(() =>
            {
                Disks.Clear();
                foreach (var d in disks)
                    Disks.Add(d);
            });

            _log.Log($"Found {disks.Count} disk(s).");

            if (Disks.Count > 0 && SelectedDisk is null)
                SelectedDisk = Disks[0];
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

    public async Task LoadPartitionsAsync()
    {
        if (SelectedDisk is null) return;

        IsBusy = true;
        try
        {
            var disk = SelectedDisk;
            _log.Log($"Loading partitions for Disk {disk.Number}...");

            var parts = await _wmiService.GetPartitionsAsync(disk.Number);
            var vols = await _wmiService.GetVolumesAsync();
            WmiDiskService.EnrichPartitionsWithVolumes(parts, vols);

            // Detect pagefiles
            var pagefileLetters = await _wmiService.GetPagefileLocationsAsync();
            foreach (var p in parts)
            {
                if (p.DriveLetter.HasValue && pagefileLetters.Contains(p.DriveLetter.Value))
                    p.HasPagefile = true;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                Partitions.Clear();
                foreach (var p in parts)
                    Partitions.Add(p);
            });

            ComputeDiskBarSegments(disk, parts);
            _log.Log($"Loaded {parts.Count} partition(s) for Disk {disk.Number}.");
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
                    Label = $"Unallocated ({SizeUtil.Format(gap)})",
                    ColorHex = SegmentColors["Unallocated"],
                });
            }

            string label = part.DriveLetter.HasValue
                ? $"{part.DriveLetter}: ({SizeUtil.Format(part.Size)})"
                : $"{part.Type} ({SizeUtil.Format(part.Size)})";

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
                Label = $"Unallocated ({SizeUtil.Format(gap)})",
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
            MessageBox.Show($"Failed to create partition:\n{ex.Message}", "Create Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExecuteFormatAsync(char letter, string fs, string label, bool quick)
    {
        IsBusy = true;
        try
        {
            _log.Log($"Formatting {letter}: as {fs} (label=\"{label}\", quick={quick})...");

            string script = $"""
                select volume {letter}
                format fs={fs} label="{label}" {(quick ? "quick" : "")}
                """;

            var result = await _processRunner.RunDiskpartAsync(script, _log);
            _log.Log($"Format result: {result.Trim()}");
            await LoadPartitionsAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"Format failed: {ex.Message}");
            MessageBox.Show($"Failed to format volume:\n{ex.Message}", "Format Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
            long newSizeMB = newSizeBytes / (1024 * 1024);
            _log.Log($"Resizing {letter}: to {SizeUtil.Format(newSizeBytes)}...");

            var cmd = $"Resize-Partition -DriveLetter '{letter}' -Size {newSizeBytes}";
            var result = await _processRunner.RunPowerShellAsync(cmd, _log);
            _log.Log($"Resize result: {result.Trim()}");
            await LoadPartitionsAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"Resize failed: {ex.Message}");
            MessageBox.Show($"Failed to resize partition:\n{ex.Message}", "Resize Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
            _log.Log($"Splitting {letter}: — shrink by {newPartGB:F2} GB, new partition {newLetter}:...");

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
            MessageBox.Show($"Failed to split partition:\n{ex.Message}", "Split Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show($"Failed to change drive letter:\n{ex.Message}", "Change Letter Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
        var result = MessageBox.Show(
            $"Delete partition {part.PartitionNumber} on Disk {SelectedDisk.Number}?\n" +
            $"Letter: {part.LetterDisplay}, Size: {part.SizeText}\n\n" +
            "ALL DATA ON THIS PARTITION WILL BE LOST.",
            "Confirm Delete",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            _log.Log($"Deleting partition {part.PartitionNumber} on Disk {SelectedDisk.Number}...");

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
            MessageBox.Show($"Failed to delete partition:\n{ex.Message}", "Delete Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
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

        var confirm = MessageBox.Show(msg, "Confirm Extend",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
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
                // No drive letter — use diskpart
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
            MessageBox.Show($"Failed to extend partition:\n{ex.Message}", "Extend Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
        var confirm = MessageBox.Show(
            $"Set partition {part.PartitionNumber} on Disk {SelectedDisk.Number} as ACTIVE?\n\n" +
            "Warning: Setting the wrong partition as active can prevent Windows from booting.",
            "Confirm Set Active",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

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
            MessageBox.Show($"Failed to set partition as active:\n{ex.Message}", "Set Active Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
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

        var confirm = MessageBox.Show(
            $"{action} partition {part.PartitionNumber} ({part.LetterDisplay}) on Disk {SelectedDisk.Number}?",
            $"Confirm {action}",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

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
            MessageBox.Show($"Failed to {action.ToLower()} partition:\n{ex.Message}", $"{action} Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
