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
    public OperationQueue Queue { get; } = new();

    private DiskInfo? _selectedDisk;
    public DiskInfo? SelectedDisk
    {
        get => _selectedDisk;
        set
        {
            if (SetProperty(ref _selectedDisk, value))
            {
                OnPropertyChanged(nameof(HasSelectedDisk));
                OnPropertyChanged(nameof(IsSelectedDiskRaw));
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

    public bool IsSelectedDiskRaw => SelectedDisk?.IsRaw == true;
    public bool HasPendingOperations => Queue.HasPending;
    public string PendingCountText => Queue.SummaryText;

    // Commands
    public ICommand RefreshCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ExtendCommand { get; }
    public ICommand SetActiveCommand { get; }
    public ICommand HideCommand { get; }
    public ICommand InitializeDiskCommand { get; }
    public ICommand ApplyQueueCommand { get; }
    public ICommand ClearQueueCommand { get; }

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
        InitializeDiskCommand = new AsyncRelayCommand(_ => ExecuteInitializeDiskAsync(), _ => SelectedDisk?.IsRaw == true);
        ApplyQueueCommand = new AsyncRelayCommand(_ => ApplyQueueAsync(), _ => Queue.HasPending);
        ClearQueueCommand = new RelayCommand(_ => ClearQueue(), _ => Queue.HasPending);

        Queue.Pending.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasPendingOperations));
            OnPropertyChanged(nameof(PendingCountText));
            CommandManager.InvalidateRequerySuggested();
        };
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
            var poolMembership = await _wmiService.GetStoragePoolMembershipAsync();
            foreach (var disk in disks)
            {
                if (poolMembership.TryGetValue(disk.Number, out var poolName))
                    disk.StoragePoolName = poolName;
            }

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

    public Task ExecuteCreateAsync(double sizeGB, char letter, string fs, string label, bool quick)
    {
        if (SelectedDisk is null) return Task.CompletedTask;
        if (!GuardStoragePool("Create partition")) return Task.CompletedTask;

        int diskNum = SelectedDisk.Number;
        letter = ProcessRunner.ValidateDriveLetter(letter);
        label = ProcessRunner.SanitizeLabel(label);
        fs = ProcessRunner.ValidateFileSystem(fs);

        Queue.Enqueue(new PendingOperation
        {
            Type = PendingOperationType.Create,
            Description = $"Create {sizeGB:F1} GB {fs} partition ({letter}:) on Disk {diskNum}",
            DiskTarget = $"Disk {diskNum}",
            Execute = async () =>
            {
                long sizeMB = (long)(sizeGB * 1024);
                _log.Log($"Creating partition on Disk {diskNum}: {sizeGB:F2} GB, {fs}, letter={letter}...");
                string script = $"""
                    select disk {diskNum}
                    create partition primary size={sizeMB}
                    assign letter={letter}
                    format fs={fs} label="{label}" {(quick ? "quick" : "")}
                    """;
                await _processRunner.RunDiskpartAsync(script, _log);
            }
        });

        _log.Log($"Queued: Create partition on Disk {diskNum}");
        return Task.CompletedTask;
    }

    public Task ExecuteFormatAsync(char letter, string fs, string label, bool quick, string? allocationUnitSize = null)
    {
        letter = ProcessRunner.ValidateDriveLetter(letter);
        label = ProcessRunner.SanitizeLabel(label);
        fs = ProcessRunner.ValidateFileSystem(fs);
        allocationUnitSize = ProcessRunner.ValidateAllocationUnitSize(allocationUnitSize);
        var partition = FindPartitionByLetter(letter);
        if (!GuardUnsupportedType(partition, $"Format {letter}:"))
            return Task.CompletedTask;
        if (!ConfirmBitLockerDestructiveOperation(partition, $"Format {letter}:"))
            return Task.CompletedTask;

        var diskNum = SelectedDisk?.Number;

        Queue.Enqueue(new PendingOperation
        {
            Type = PendingOperationType.Format,
            Description = $"Format {letter}: as {fs}",
            DiskTarget = diskNum.HasValue ? $"Disk {diskNum}" : $"Volume {letter}:",
            RiskLevel = "Destructive",
            Execute = async () =>
            {
                _log.Log($"Formatting {letter}: as {fs} (label=\"{label}\", quick={quick})...");
                if (diskNum.HasValue)
                    await _backup.SaveSnapshotAsync(diskNum.Value);
                using var volumeLock = VolumeLockService.RequireLock(letter, _log);
                var unitParam = !string.IsNullOrEmpty(allocationUnitSize) ? $"unit={allocationUnitSize} " : "";
                string script = $"""
                    select volume {letter}
                    format fs={fs} label="{label}" {unitParam}{(quick ? "quick" : "")}
                    """;
                await _processRunner.RunDiskpartAsync(script, _log);
            }
        });

        _log.Log($"Queued: Format {letter}: as {fs}");
        return Task.CompletedTask;
    }

    public Task ExecuteResizeAsync(char letter, long newSizeBytes)
    {
        letter = ProcessRunner.ValidateDriveLetter(letter);
        var partition = FindPartitionByLetter(letter);
        if (!GuardBitLockerMutation(partition, $"Resize {letter}:"))
            return Task.CompletedTask;

        Queue.Enqueue(new PendingOperation
        {
            Type = PendingOperationType.Resize,
            Description = $"Resize {letter}: to {SizeUtil.Format(newSizeBytes)}",
            DiskTarget = $"Volume {letter}:",
            Execute = async () =>
            {
                _log.Log($"Resizing {letter}: to {SizeUtil.Format(newSizeBytes)}...");
                using var volumeLock = VolumeLockService.RequireLock(letter, _log);
                var cmd = $"Resize-Partition -DriveLetter '{letter}' -Size {newSizeBytes}";
                await _processRunner.RunPowerShellAsync(cmd, _log);
            }
        });

        _log.Log($"Queued: Resize {letter}: to {SizeUtil.Format(newSizeBytes)}");
        return Task.CompletedTask;
    }

    public Task ExecuteSplitAsync(char letter, double newPartGB, char newLetter, string fs, string label)
    {
        letter = ProcessRunner.ValidateDriveLetter(letter);
        newLetter = ProcessRunner.ValidateDriveLetter(newLetter);
        label = ProcessRunner.SanitizeLabel(label);
        fs = ProcessRunner.ValidateFileSystem(fs);
        var partition = FindPartitionByLetter(letter);
        if (!GuardBitLockerMutation(partition, $"Split {letter}:"))
            return Task.CompletedTask;

        var diskNum = SelectedDisk?.Number;

        Queue.Enqueue(new PendingOperation
        {
            Type = PendingOperationType.Split,
            Description = $"Split {letter}: — shrink by {newPartGB:F1} GB, new {newLetter}: ({fs})",
            DiskTarget = diskNum.HasValue ? $"Disk {diskNum}" : $"Volume {letter}:",
            Execute = async () =>
            {
                if (diskNum.HasValue)
                    await _backup.SaveSnapshotAsync(diskNum.Value);
                _log.Log($"Splitting {letter}: shrink by {newPartGB:F2} GB, new partition {newLetter}:...");
                using var volumeLock = VolumeLockService.RequireLock(letter, _log);
                long shrinkMB = (long)(newPartGB * 1024);
                var shrinkCmd = $"Resize-Partition -DriveLetter '{letter}' -Size ((Get-Partition -DriveLetter '{letter}').Size - {shrinkMB * 1024 * 1024})";
                await _processRunner.RunPowerShellAsync(shrinkCmd, _log);
                if (!diskNum.HasValue) return;
                string script = $"""
                    select disk {diskNum}
                    create partition primary size={shrinkMB}
                    assign letter={newLetter}
                    format fs={fs} label="{label}" quick
                    """;
                await _processRunner.RunDiskpartAsync(script, _log);
            }
        });

        _log.Log($"Queued: Split {letter}:");
        return Task.CompletedTask;
    }

    public Task ExecuteChangeLetterAsync(int partNum, char newLetter)
    {
        if (SelectedDisk is null) return Task.CompletedTask;
        if (partNum <= 0)
            throw new ArgumentException($"Invalid partition number: {partNum}", nameof(partNum));
        newLetter = ProcessRunner.ValidateDriveLetter(newLetter);

        var diskNum = SelectedDisk.Number;
        var partition = Partitions.FirstOrDefault(p => p.PartitionNumber == partNum);
        var oldLetter = partition?.DriveLetter;

        Queue.Enqueue(new PendingOperation
        {
            Type = PendingOperationType.ChangeLetter,
            Description = $"Change letter: partition {partNum} on Disk {diskNum} to {newLetter}:",
            DiskTarget = $"Disk {diskNum}",
            Execute = async () =>
            {
                _log.Log($"Changing drive letter for Disk {diskNum}, Partition {partNum} to {newLetter}:...");
                string script;
                if (oldLetter.HasValue)
                {
                    script = $"""
                        select volume {oldLetter}
                        remove letter={oldLetter}
                        assign letter={newLetter}
                        """;
                }
                else
                {
                    script = $"""
                        select disk {diskNum}
                        select partition {partNum}
                        assign letter={newLetter}
                        """;
                }
                await _processRunner.RunDiskpartAsync(script, _log);
            }
        });

        _log.Log($"Queued: Change letter on Disk {diskNum} partition {partNum} to {newLetter}:");
        return Task.CompletedTask;
    }

    // ──────────────────────── Queue Apply / Clear ────────────────────

    private async Task ApplyQueueAsync()
    {
        if (!Queue.HasPending) return;

        var summary = string.Join("\n", Queue.Pending.Select((op, i) => $"  {i + 1}. {op.Description}"));
        if (!_dialog.ConfirmWarning(
            $"Apply {Queue.Count} pending operation(s)?\n\n{summary}\n\nOperations will execute in order. This cannot be undone.",
            "Apply Pending Operations"))
            return;

        await Queue.ApplyAllAsync(_log, _dialog,
            busy => IsBusy = busy,
            status => PendingOperation = status);

        await LoadDisksAsync();
    }

    private void ClearQueue()
    {
        if (!Queue.HasPending) return;
        Queue.Clear();
        _log.Log("Pending operations cleared.");
    }

    // ──────────────────────── Initialize Disk ────────────────────────

    private async Task ExecuteInitializeDiskAsync()
    {
        if (SelectedDisk is null || !SelectedDisk.IsRaw) return;

        var diskNum = SelectedDisk.Number;
        var diskName = SelectedDisk.FriendlyName;
        var diskSize = SizeUtil.Format(SelectedDisk.Size);

        if (!_dialog.Confirm(
            $"Initialize Disk {diskNum} ({diskName}, {diskSize}) with a GPT partition table?\n\n" +
            "This will write an empty GPT partition table to the disk. " +
            "Use MBR only if legacy BIOS boot compatibility is required.",
            "Initialize Disk")) return;

        IsBusy = true;
        try
        {
            _log.Log($"Initializing Disk {diskNum} as GPT...");
            var cmd = $"Initialize-Disk -Number {diskNum} -PartitionStyle GPT -Confirm:$false";
            await _processRunner.RunPowerShellAsync(cmd, _log);
            _log.Log($"Disk {diskNum} initialized as GPT.");
            _dialog.ShowInfo($"Disk {diskNum} initialized with a GPT partition table.", "Disk Initialized");
            await LoadDisksAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"Initialize disk failed: {ex.Message}");
            _dialog.ShowError($"Failed to initialize disk:\n{ex.Message}", "Initialize Error");
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
        var diskNum = SelectedDisk.Number;
        if (!GuardStoragePool($"Delete partition {part.PartitionNumber}"))
            return;
        if (!await GuardRecoveryPartitionOperationAsync(part, "delete"))
            return;
        if (!GuardUnsupportedType(part, $"Delete partition {part.PartitionNumber}"))
            return;

        var encryptionLine = string.IsNullOrWhiteSpace(part.EncryptionStatus)
            ? ""
            : $"\nEncryption: {part.EncryptionStatus}";

        if (part.IsCritical)
        {
            if (!_dialog.ConfirmDanger(
                $"CRITICAL: Partition {part.PartitionNumber} is a {part.Type} partition" +
                (part.IsBoot ? " (Boot)" : "") + (part.IsSystem ? " (System)" : "") +
                $".\n\nDeleting it may make the system unbootable.\n\nDisk: {diskNum}, Letter: {part.LetterDisplay}, Size: {part.SizeText}{encryptionLine}\n\n" +
                "Type YES to confirm this destructive action.",
                "Delete Critical Partition")) return;
        }

        if (!ConfirmBitLockerDestructiveOperation(part, $"Delete partition {part.PartitionNumber}"))
            return;

        if (!_dialog.ConfirmWarning(
            $"Delete partition {part.PartitionNumber} on Disk {diskNum}?\n" +
            $"Letter: {part.LetterDisplay}, Size: {part.SizeText}{encryptionLine}\n\n" +
            "ALL DATA ON THIS PARTITION WILL BE LOST.\n\n" +
            "The deletion will be queued and applied when you click Apply.",
            "Confirm Delete")) return;

        var partNum = part.PartitionNumber;
        var driveLetter = part.DriveLetter;

        Queue.Enqueue(new PendingOperation
        {
            Type = PendingOperationType.Delete,
            Description = $"Delete partition {partNum} ({part.LetterDisplay}) on Disk {diskNum}",
            DiskTarget = $"Disk {diskNum}",
            RiskLevel = "Destructive",
            Execute = async () =>
            {
                await _backup.SaveSnapshotAsync(diskNum);
                using var volumeLock = driveLetter.HasValue
                    ? VolumeLockService.RequireLock(driveLetter.Value, _log)
                    : null;
                string script = $"""
                    select disk {diskNum}
                    select partition {partNum}
                    delete partition override
                    """;
                await _processRunner.RunDiskpartAsync(script, _log);
            }
        });

        _log.Log($"Queued: Delete partition {partNum} on Disk {diskNum}");
    }

    private async Task ExecuteExtendAsync()
    {
        if (SelectedPartition is null || SelectedDisk is null) return;

        var part = SelectedPartition;
        if (!await GuardRecoveryPartitionOperationAsync(part, "extend"))
            return;

        if (!GuardBitLockerMutation(part, $"Extend partition {part.PartitionNumber}"))
            return;

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
            using var volumeLock = part.DriveLetter.HasValue
                ? VolumeLockService.RequireLock(part.DriveLetter.Value, _log)
                : null;

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

    public static bool IsRecoveryPartition(PartitionInfo partition) =>
        partition.Type.Equals("Recovery", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> GuardRecoveryPartitionOperationAsync(PartitionInfo partition, string operation)
    {
        if (!IsRecoveryPartition(partition))
            return true;

        string reagentInfo;
        try
        {
            reagentInfo = (await _processRunner.RunExeAsync("reagentc", "/info", _log)).Trim();
            _log.Log($"reagentc /info before Recovery partition {operation}: {reagentInfo}");
        }
        catch (Exception ex)
        {
            reagentInfo = $"Unable to read Windows RE status: {ex.Message}";
            _log.Log(reagentInfo);
        }

        _dialog.ShowError(
            $"PartitionPilot will not {operation} a Recovery partition automatically because that can disable Windows RE or remove the recovery environment.\n\n" +
            "Use a dedicated recovery relocation workflow before changing this partition.\n\n" +
            reagentInfo,
            "Recovery Environment Guard");
        return false;
    }

    private PartitionInfo? FindPartitionByLetter(char letter)
    {
        letter = char.ToUpperInvariant(letter);
        return Partitions.FirstOrDefault(p => p.DriveLetter.HasValue && char.ToUpperInvariant(p.DriveLetter.Value) == letter);
    }

    private bool GuardStoragePool(string operation)
    {
        if (SelectedDisk is null || !SelectedDisk.IsPooled)
            return true;

        return _dialog.ConfirmDanger(
            $"{operation} targets Disk {SelectedDisk.Number} which belongs to Storage Spaces pool \"{SelectedDisk.StoragePoolName}\".\n\n" +
            "Modifying pooled disks can break pool integrity and cause data loss across the entire pool. " +
            "Use Windows Storage Spaces management to modify pooled disks.",
            "Storage Spaces Pool Warning");
    }

    private bool GuardUnsupportedType(PartitionInfo? partition, string operation)
    {
        if (partition is null || !partition.IsUnsupportedType)
            return true;

        if (!_dialog.ConfirmDanger(
            $"{operation} targets a {partition.Type} partition that PartitionPilot cannot manage natively.\n\n" +
            $"Partition: {partition.PartitionDisplay}, Size: {partition.SizeText}\n\n" +
            "Modifying this partition may destroy data that Windows cannot read or recover. " +
            "Proceed only if you are certain this partition is no longer needed.",
            $"Unsupported Partition Type: {partition.Type}"))
        {
            _log.Log($"{operation} cancelled — unsupported partition type: {partition.Type}");
            return false;
        }

        return true;
    }

    private bool GuardBitLockerMutation(PartitionInfo? partition, string operation)
    {
        if (partition is null || !BitLockerPreflight.RequiresSuspensionForMutation(partition.EncryptionStatus))
            return true;

        _log.Log($"{operation} blocked by BitLocker state: {BitLockerPreflight.Describe(partition.EncryptionStatus)}");
        _dialog.ShowError(
            BitLockerPreflight.BuildMutationBlockedMessage(operation, partition.PartitionDisplay, partition.EncryptionStatus),
            "BitLocker Protection Active");
        return false;
    }

    private bool ConfirmBitLockerDestructiveOperation(PartitionInfo? partition, string operation)
    {
        if (partition is null || !BitLockerPreflight.IsProtected(partition.EncryptionStatus))
            return true;

        return _dialog.ConfirmDanger(
            BitLockerPreflight.BuildDestructiveConfirmation(
                operation,
                new[] { BitLockerPreflight.DescribePartitionTarget(partition) }),
            "Confirm BitLocker-Protected Data Loss");
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
