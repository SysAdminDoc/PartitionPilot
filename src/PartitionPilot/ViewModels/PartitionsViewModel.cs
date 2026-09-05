using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace PartitionPilot;

public class PartitionsViewModel : ViewModelBase
{
    private readonly IWmiDiskService _wmiService;
    private readonly ProcessRunner _processRunner;
    private readonly IActivityLog _log;
    private readonly IDialogService _dialog;
    private readonly PartitionTableBackup _backup;
    private readonly Action<Action> _invokeOnUiThread;
    private CancellationTokenSource? _loadCts;
    private int _activeLoadCount;

    public ObservableCollection<DiskInfo> Disks { get; } = new();
    public ObservableCollection<PartitionInfo> Partitions { get; } = new();
    public ObservableCollection<DiskBarSegment> DiskBarSegments { get; } = new();
    public OperationQueue Queue { get; } = new();
    private bool _journalCheckDone;

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
        ? LocExtension.Get("SelectDiskPrompt")
        : LocExtension.Format("DiskSummaryFormat",
            SizeUtil.Format(SelectedDisk.Size), SelectedDisk.PartitionStyle, SelectedDisk.NumberOfPartitions,
            SizeUtil.Format(SelectedDisk.LargestFreeExtent));

    public string DiskCapacityText => SelectedDisk is null
        ? LocExtension.Get("NoDiskSelected")
        : LocExtension.Format("DiskTotalFormat", SizeUtil.Format(SelectedDisk.Size));

    public string DiskFreeExtentText => SelectedDisk is null
        ? LocExtension.Get("RefreshDisksHint")
        : LocExtension.Format("LargestFreeExtentFormat", SizeUtil.Format(SelectedDisk.LargestFreeExtent));

    public string DiskPartitionStyleText => SelectedDisk is null
        ? LocExtension.Get("PartitionStyleUnavailable")
        : LocExtension.Format("PartitionStyleFormat", SelectedDisk.PartitionStyle);

    public string SelectedPartitionName => SelectedPartition is null
        ? LocExtension.Get("NoPartitionSelected")
        : SelectedPartition.PartitionDisplay;

    public string SelectedPartitionSummary
    {
        get
        {
            if (SelectedPartition is null)
                return LocExtension.Get("SelectPartitionPrompt");

            var fileSystem = string.IsNullOrWhiteSpace(SelectedPartition.FileSystem)
                ? LocExtension.Get("NoFileSystem")
                : SelectedPartition.FileSystem;
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
    public ICommand RemoveQueuedOperationCommand { get; }

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

    public PartitionsViewModel(
        IWmiDiskService wmiService,
        ProcessRunner processRunner,
        IActivityLog log,
        IDialogService dialog,
        Action<Action>? invokeOnUiThread = null)
    {
        _wmiService = wmiService;
        _processRunner = processRunner;
        _log = log;
        _dialog = dialog;
        _backup = new PartitionTableBackup(wmiService, log);
        _invokeOnUiThread = invokeOnUiThread ?? InvokeOnUiThread;

        RefreshCommand = new AsyncRelayCommand(_ => LoadDisksAsync());
        DeleteCommand = new AsyncRelayCommand(_ => ExecuteDeleteAsync(), _ => SelectedPartition is not null);
        ExtendCommand = new AsyncRelayCommand(_ => ExecuteExtendAsync(), _ => SelectedPartition is not null);
        SetActiveCommand = new AsyncRelayCommand(_ => ExecuteSetActiveAsync(), _ => SelectedPartition is not null);
        HideCommand = new AsyncRelayCommand(_ => ExecuteHideToggleAsync(), _ => SelectedPartition is not null);
        InitializeDiskCommand = new AsyncRelayCommand(_ => ExecuteInitializeDiskAsync(), _ => SelectedDisk?.IsRaw == true);
        ApplyQueueCommand = new AsyncRelayCommand(_ => ApplyQueueAsync(), _ => Queue.HasPending);
        ClearQueueCommand = new WpfRelayCommand(_ => ClearQueue(), _ => Queue.HasPending);
        RemoveQueuedOperationCommand = new WpfRelayCommand(
            op => RemoveQueuedOperation(op as PendingOperation),
            op => op is PendingOperation);

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

    /// <summary>
    /// Finds what is holding the shrink floor up, if Windows has recorded a shrink analysis for this
    /// volume. Returns null when nothing is blocking, nothing was recorded, or the log cannot be read.
    /// </summary>
    public async Task<(ShrinkBlocker Blocker, int BytesPerCluster)?> FindShrinkBlockerAsync(char letter, long minimumSize)
    {
        if (minimumSize <= 0)
            return null;

        try
        {
            var blocker = await ShrinkBlockerService.FindLatestBlockerAsync(letter, _processRunner, _log);
            if (blocker is null)
                return null;

            var bytesPerCluster = await ReadBytesPerClusterAsync(letter);
            return (blocker, bytesPerCluster);
        }
        catch (Exception ex)
        {
            _log.Log($"Shrink blocker lookup failed for {letter}: {ex.Message}");
            return null;
        }
    }

    private async Task<int> ReadBytesPerClusterAsync(char letter)
    {
        try
        {
            var text = await _processRunner.RunPowerShellAsync(
                $"(Get-Volume -DriveLetter '{letter}').AllocationUnitSize", _log);
            return int.TryParse(text.Trim(), out var size) && size > 0 ? size : 4096;
        }
        catch
        {
            return 4096;
        }
    }

    // ──────────────────────── Load Methods ────────────────────────

    public async Task LoadDisksAsync()
    {
        BeginLoad();
        try
        {
            if (!_journalCheckDone)
            {
                _journalCheckDone = true;
                await CheckInterruptedJournalsAsync();
            }

            _log.Log("Refreshing disk list...");
            var priorDiskNumber = SelectedDisk?.Number;
            var disks = await _wmiService.GetDisksAsync();
            var poolMembership = await _wmiService.GetStoragePoolMembershipAsync();
            var poolHealth = await _wmiService.GetStoragePoolHealthAsync();
            foreach (var disk in disks)
            {
                if (poolMembership.TryGetValue(disk.Number, out var poolName))
                {
                    disk.StoragePoolName = poolName;
                    if (poolHealth.TryGetValue(poolName, out var health))
                    {
                        disk.StoragePoolHealth = health.Health;
                        disk.StoragePoolStatus = health.Status;
                        disk.StoragePoolReadOnly = health.ReadOnly;
                    }
                }
            }

            _invokeOnUiThread(() =>
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
            EndLoad();
        }
    }

    public Task LoadPartitionsAsync() => LoadPartitionsAsync(CancellationToken.None);

    private async Task LoadPartitionsAsync(CancellationToken ct)
    {
        if (SelectedDisk is null)
        {
            _invokeOnUiThread(() =>
            {
                Partitions.Clear();
                DiskBarSegments.Clear();
            });
            return;
        }

        BeginLoad();
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
            _invokeOnUiThread(() =>
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
            EndLoad();
        }
    }

    private void BeginLoad()
    {
        if (Interlocked.Increment(ref _activeLoadCount) == 1)
            _invokeOnUiThread(() => IsBusy = true);
    }

    private void EndLoad()
    {
        if (Interlocked.Decrement(ref _activeLoadCount) == 0)
            _invokeOnUiThread(() => IsBusy = false);
    }

    private static void InvokeOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
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
        if (!GuardFilesystemCapability(FilesystemOperation.Create, fs, $"Create {fs} partition"))
            return Task.CompletedTask;

        var diskIdentity = SelectedDisk.ToIdentitySnapshot();
        Queue.Enqueue(new PendingOperation
        {
            Type = PendingOperationType.Create,
            Description = LocExtension.Format("QueueDescCreate",
                sizeGB.ToString("F1", CultureInfo.CurrentCulture), fs, letter, diskNum),
            DiskTarget = DiskTargetText(diskIdentity, LocExtension.Format("DiskTargetFallback", diskNum)),
            DiskIdentity = diskIdentity,
            ValidateTarget = BuildTargetValidator(diskIdentity),
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
        if (!GuardUnsupportedType(partition, LocExtension.Format("OpFormatDrive", letter)))
            return Task.CompletedTask;
        if (!ConfirmBitLockerDestructiveOperation(partition, LocExtension.Format("OpFormatDrive", letter)))
            return Task.CompletedTask;
        if (!GuardFilesystemCapability(FilesystemOperation.Format, fs, LocExtension.Format("OpFormatDrive", letter)))
            return Task.CompletedTask;

        var diskNum = SelectedDisk?.Number;
        var diskIdentity = FindDiskForPartition(partition)?.ToIdentitySnapshot();

        Queue.Enqueue(new PendingOperation
        {
            Type = PendingOperationType.Format,
            Description = LocExtension.Format("QueueDescFormat", letter, fs),
            DiskTarget = DiskTargetText(diskIdentity, diskNum.HasValue
                ? LocExtension.Format("DiskTargetFallback", diskNum)
                : LocExtension.Format("VolumeTargetFallback", letter)),
            DiskIdentity = diskIdentity,
            ValidateTarget = BuildTargetValidator(diskIdentity),
            RiskLevel = "Destructive",
            Execute = async () =>
            {
                _log.Log($"Formatting {letter}: as {fs} (label=\"{label}\", quick={quick})...");
                if (diskNum.HasValue)
                    await _backup.SaveSnapshotAsync(diskNum.Value);
                using var volumeLock = VolumeLockService.RequireLock(letter, _log);

                var part = FindPartitionByLetter(letter);
                bool isLargeFat32 = fs.Equals("FAT32", StringComparison.OrdinalIgnoreCase) &&
                                    part is not null && part.Size > 32L * 1024 * 1024 * 1024;

                if (isLargeFat32)
                {
                    var clusterSize = part!.Size switch
                    {
                        > 2L * 1024 * 1024 * 1024 * 1024 => 65536,
                        > 32L * 1024 * 1024 * 1024 => 32768,
                        _ => 4096
                    };
                    _log.Log($"Using format.com for FAT32 >32GB (cluster size {clusterSize})");
                    var labelArg = string.IsNullOrEmpty(label) ? "" : ProcessRunner.EscapePowerShellString(label);
                    var ps = $"Format-Volume -DriveLetter '{letter}' -FileSystem FAT32 -AllocationUnitSize {clusterSize}{(string.IsNullOrEmpty(label) ? "" : $" -NewFileSystemLabel {labelArg}")} -Force -Confirm:$false";
                    await _processRunner.RunPowerShellAsync(ps, _log);
                }
                else
                {
                    var unitParam = !string.IsNullOrEmpty(allocationUnitSize) ? $"unit={allocationUnitSize} " : "";
                    string script = $"""
                        select volume {letter}
                        format fs={fs} label="{label}" {unitParam}{(quick ? "quick" : "")}
                        """;
                    await _processRunner.RunDiskpartAsync(script, _log);
                }
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
        if (!GuardFilesystemCapability(FilesystemOperation.Resize, partition?.FileSystem, $"Resize {letter}:"))
            return Task.CompletedTask;
        var diskIdentity = FindDiskForPartition(partition)?.ToIdentitySnapshot();

        Queue.Enqueue(new PendingOperation
        {
            Type = PendingOperationType.Resize,
            Description = LocExtension.Format("QueueDescResize", letter, SizeUtil.Format(newSizeBytes)),
            DiskTarget = DiskTargetText(diskIdentity, LocExtension.Format("VolumeTargetFallback", letter)),
            DiskIdentity = diskIdentity,
            ValidateTarget = BuildTargetValidator(diskIdentity),
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
        if (!GuardFilesystemCapability(FilesystemOperation.Resize, partition?.FileSystem, $"Split {letter}:"))
            return Task.CompletedTask;
        if (!GuardFilesystemCapability(FilesystemOperation.Create, fs, $"Create split target {newLetter}:"))
            return Task.CompletedTask;

        var diskNum = SelectedDisk?.Number;
        var diskIdentity = FindDiskForPartition(partition)?.ToIdentitySnapshot();

        Queue.Enqueue(new PendingOperation
        {
            Type = PendingOperationType.Split,
            Description = LocExtension.Format("QueueDescSplit",
                letter, newPartGB.ToString("F1", CultureInfo.CurrentCulture), newLetter, fs),
            DiskTarget = DiskTargetText(diskIdentity, diskNum.HasValue
                ? LocExtension.Format("DiskTargetFallback", diskNum)
                : LocExtension.Format("VolumeTargetFallback", letter)),
            DiskIdentity = diskIdentity,
            ValidateTarget = BuildTargetValidator(diskIdentity),
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
        var diskIdentity = SelectedDisk.ToIdentitySnapshot();

        Queue.Enqueue(new PendingOperation
        {
            Type = PendingOperationType.ChangeLetter,
            Description = LocExtension.Format("QueueDescChangeLetter", partNum, diskNum, newLetter),
            DiskTarget = DiskTargetText(diskIdentity, LocExtension.Format("DiskTargetFallback", diskNum)),
            DiskIdentity = diskIdentity,
            ValidateTarget = BuildTargetValidator(diskIdentity),
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

    public async Task ExecuteMergeAsync(PartitionInfo primary, PartitionInfo secondary)
    {
        if (SelectedDisk is null) return;
        if (!IsForwardAdjacentMergePair(Partitions, primary, secondary))
        {
            _dialog.ShowWarning(
                LocExtension.Get("MergeNotAvailableBody"),
                LocExtension.Get("MergeNotAvailableTitle"));
            return;
        }
        if (!await GuardRecoveryPartitionOperationAsync(primary, "merge", "VerbMerge")) return;
        if (!await GuardRecoveryPartitionOperationAsync(secondary, "merge", "VerbMerge")) return;
        if (!GuardUnsupportedType(primary, LocExtension.Format("OpMergeInto", primary.LetterDisplay))) return;
        if (!GuardUnsupportedType(secondary, LocExtension.Format("OpDeleteToMerge", secondary.LetterDisplay))) return;
        if (!GuardStoragePool(LocExtension.Get("OpMergePartitions"))) return;
        if (!GuardFilesystemCapability(FilesystemOperation.Extend, primary.FileSystem, LocExtension.Format("OpMergeInto", primary.LetterDisplay))) return;
        if (!GuardBitLockerMutation(primary, LocExtension.Format("OpMergeInto", primary.LetterDisplay))) return;
        if (!ConfirmBitLockerDestructiveOperation(secondary, LocExtension.Format("OpDeleteToMerge", secondary.LetterDisplay))) return;

        var diskNum = SelectedDisk.Number;
        var primaryLetter = primary.DriveLetter!.Value;
        var secondaryPartNum = secondary.PartitionNumber;
        var secondaryLetter = secondary.DriveLetter;
        var diskIdentity = SelectedDisk.ToIdentitySnapshot();

        Queue.Enqueue(new PendingOperation
        {
            Type = PendingOperationType.Delete,
            Description = LocExtension.Format("QueueDescMerge",
                secondaryPartNum, secondary.LetterDisplay, primaryLetter, diskNum),
            DiskTarget = DiskTargetText(diskIdentity, LocExtension.Format("DiskTargetFallback", diskNum)),
            DiskIdentity = diskIdentity,
            ValidateTarget = BuildTargetValidator(diskIdentity),
            RiskLevel = "Destructive",
            Execute = async () =>
            {
                await _backup.SaveSnapshotAsync(diskNum);
                _log.Log($"Merge: deleting secondary partition {secondaryPartNum} on Disk {diskNum}...");
                using var secondaryLock = secondaryLetter.HasValue
                    ? VolumeLockService.RequireLock(secondaryLetter.Value, _log)
                    : null;
                string deleteScript = $"""
                    select disk {diskNum}
                    select partition {secondaryPartNum}
                    delete partition override
                    """;
                await _processRunner.RunDiskpartAsync(deleteScript, _log);
                _log.Log($"Merge: extending {primaryLetter}: to fill freed space...");
                var sizeInfo = await _wmiService.GetPartitionSupportedSizeAsync(primaryLetter);
                var extendCmd = $"Resize-Partition -DriveLetter '{primaryLetter}' -Size {sizeInfo.Max}";
                await _processRunner.RunPowerShellAsync(extendCmd, _log);
                _log.Log($"Merge complete: {primaryLetter}: extended to {SizeUtil.Format(sizeInfo.Max)}.");
            }
        });

        _log.Log($"Queued: Merge partition {secondaryPartNum} into {primaryLetter}: on Disk {diskNum}");
    }

    public static bool IsForwardAdjacentMergePair(IEnumerable<PartitionInfo> partitions, PartitionInfo primary, PartitionInfo secondary)
    {
        if (ReferenceEquals(primary, secondary)) return false;
        if (!primary.DriveLetter.HasValue || !secondary.DriveLetter.HasValue) return false;
        if (primary.DiskNumber != secondary.DiskNumber) return false;
        if (primary.Offset >= secondary.Offset) return false;

        var ordered = partitions
            .Where(p => p.DiskNumber == primary.DiskNumber)
            .OrderBy(p => p.Offset)
            .ThenBy(p => p.PartitionNumber)
            .ToList();

        var primaryIndex = ordered.FindIndex(p => IsSamePartition(p, primary));
        if (primaryIndex < 0 || primaryIndex + 1 >= ordered.Count) return false;

        var next = ordered[primaryIndex + 1];
        return IsSamePartition(next, secondary) && primary.Offset + primary.Size <= secondary.Offset;
    }

    private static bool IsSamePartition(PartitionInfo left, PartitionInfo right) =>
        ReferenceEquals(left, right) ||
        left.DiskNumber == right.DiskNumber &&
        left.PartitionNumber == right.PartitionNumber &&
        left.Offset == right.Offset;

    // ──────────────────────── Queue Apply / Clear ────────────────────

    private async Task ApplyQueueAsync()
    {
        if (!Queue.HasPending) return;

        var impactPreview = BuildImpactPreview();
        if (!_dialog.ConfirmWarning(impactPreview, LocExtension.Get("ApplyPendingOperationsTitle")))
            return;

        await Queue.ApplyAllAsync(_log, _dialog,
            busy => IsBusy = busy,
            status => PendingOperation = status);

        await LoadDisksAsync();
    }

    private string BuildImpactPreview()
    {
        var ops = Queue.Pending.ToList();
        var sb = new System.Text.StringBuilder();

        sb.AppendLine(LocExtension.Format("ImpactApplyHeading", ops.Count));
        sb.AppendLine();

        var highRisk = ops.Count(o => o.RiskLevel != "Normal");
        var destructive = ops.Count(o => o.Type is PendingOperationType.Delete or PendingOperationType.Format);
        var targets = ops.Select(o => o.DiskTarget).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();

        if (highRisk > 0 || destructive > 0)
        {
            sb.AppendLine(LocExtension.Get("ImpactRiskHeading"));
            if (destructive > 0)
                sb.AppendLine(LocExtension.Format("ImpactDestructiveCount", destructive));
            if (highRisk > 0)
                sb.AppendLine(LocExtension.Format("ImpactElevatedCount", highRisk));
            sb.AppendLine();
        }

        if (targets.Count > 0)
        {
            sb.AppendLine(LocExtension.Get("ImpactTargetsHeading"));
            foreach (var target in targets)
                sb.AppendLine($"  {target}");
            foreach (var identity in ops.Select(o => o.DiskIdentity).Where(i => i is not null).Cast<DiskIdentitySnapshot>().DistinctBy(i => i.DiskNumber))
                sb.AppendLine($"  {identity.StableIdentityText}");
            sb.AppendLine();
        }

        sb.AppendLine(LocExtension.Get("ImpactOperationsHeading"));
        for (int i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            var risk = op.RiskLevel != "Normal" ? $" [{op.RiskLevel}]" : "";
            sb.AppendLine($"  {i + 1}. [{op.TypeDisplay}]{risk} {op.Description}");
        }

        sb.AppendLine();
        sb.AppendLine(LocExtension.Get("ImpactFooterOrder"));
        sb.AppendLine(LocExtension.Get("ImpactFooterBackup"));

        return sb.ToString();
    }

    private DiskInfo? FindDiskForPartition(PartitionInfo? partition)
    {
        if (partition is null)
            return SelectedDisk;

        return Disks.FirstOrDefault(d => d.Number == partition.DiskNumber) ?? SelectedDisk;
    }

    private static string DiskTargetText(DiskIdentitySnapshot? identity, string fallback) =>
        identity?.Summary ?? fallback;

    private Func<Task> BuildTargetValidator(DiskIdentitySnapshot? identity) =>
        identity is null ? () => Task.CompletedTask : () => identity.VerifyCurrentAsync(_wmiService);

    private async Task CheckInterruptedJournalsAsync()
    {
        try
        {
            var interrupted = await OperationJournalService.LoadInterruptedJournalsAsync();
            if (interrupted.Count == 0) return;

            foreach (var journal in interrupted)
            {
                var entries = journal.Entries;
                var completedCount = entries.Count(e => e.Status == JournalEntryStatus.Completed);
                var failedCount = entries.Count(e => e.Status == JournalEntryStatus.Failed);
                var skippedCount = entries.Count(e => e.Status == JournalEntryStatus.Skipped || e.Status == JournalEntryStatus.Queued);

                var summary = LocExtension.Format("InterruptedJournalSummary",
                    journal.CreatedAt.ToString("g", CultureInfo.CurrentCulture),
                    completedCount, failedCount, skippedCount,
                    string.Join("\n", entries.Select(e => $"  [{e.Status}] {e.Description}")));

                _log.Log($"Interrupted journal detected: {journal.Id} ({completedCount} completed, {failedCount} failed, {skippedCount} skipped)");
                foreach (var entry in entries)
                    _log.Log($"  Journal [{entry.Status}] {entry.Description}{(entry.ErrorMessage is not null ? $" — {entry.ErrorMessage}" : "")}");

                _dialog.ShowWarning(summary, LocExtension.Get("InterruptedJournalTitle"));
                await OperationJournalService.DiscardJournalAsync(journal.Id);
            }
        }
        catch (Exception ex)
        {
            _log.Log($"Journal check failed (non-fatal): {ex.Message}");
        }
    }

    private void ClearQueue()
    {
        if (!Queue.HasPending) return;
        Queue.Clear();
        _log.Log("Pending operations cleared.");
    }

    private void RemoveQueuedOperation(PendingOperation? op)
    {
        if (op is null) return;
        Queue.Remove(op);
        _log.Log($"Removed pending operation: {op.Description}");
    }

    // ──────────────────────── Initialize Disk ────────────────────────

    private async Task ExecuteInitializeDiskAsync()
    {
        if (SelectedDisk is null || !SelectedDisk.IsRaw) return;

        var diskNum = SelectedDisk.Number;
        var diskName = SelectedDisk.FriendlyName;
        var diskSize = SizeUtil.Format(SelectedDisk.Size);

        if (!_dialog.Confirm(
            LocExtension.Format("InitializeDiskPrompt",
                diskNum, diskName, diskSize, SelectedDisk.IdentitySummary),
            LocExtension.Get("InitializeDiskTitle"))) return;

        IsBusy = true;
        try
        {
            _log.Log($"Initializing Disk {diskNum} as GPT...");
            var cmd = $"Initialize-Disk -Number {diskNum} -PartitionStyle GPT -Confirm:$false";
            await _processRunner.RunPowerShellAsync(cmd, _log);
            _log.Log($"Disk {diskNum} initialized as GPT.");
            _dialog.ShowInfo(
                LocExtension.Format("DiskInitializedBody", diskNum),
                LocExtension.Get("DiskInitializedTitle"));
            await LoadDisksAsync();
        }
        catch (Exception ex)
        {
            _log.Log($"Initialize disk failed: {ex.Message}");
            _dialog.ShowError(
                LocExtension.Format("InitializeDiskFailed", ex.Message),
                LocExtension.Get("InitializeErrorTitle"));
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
        var diskIdentity = SelectedDisk.ToIdentitySnapshot();
        if (!GuardStoragePool(LocExtension.Format("OpDeletePartition", part.PartitionNumber)))
            return;
        if (!await GuardRecoveryPartitionOperationAsync(part, "delete", "VerbDelete"))
            return;
        if (!GuardUnsupportedType(part, LocExtension.Format("OpDeletePartition", part.PartitionNumber)))
            return;

        var encryptionLine = string.IsNullOrWhiteSpace(part.EncryptionStatus)
            ? ""
            : LocExtension.Format("EncryptionLine", part.EncryptionStatus);

        if (part.IsCritical)
        {
            var flags = (part.IsBoot ? LocExtension.Get("PartitionFlagBoot") : "") +
                        (part.IsSystem ? LocExtension.Get("PartitionFlagSystem") : "");

            if (!_dialog.ConfirmDanger(
                LocExtension.Format("DeleteCriticalPrompt",
                    part.PartitionNumber, part.Type, flags, diskIdentity.ConfirmationSummary,
                    part.LetterDisplay, part.SizeText, encryptionLine),
                LocExtension.Get("DeleteCriticalTitle"))) return;
        }

        if (!ConfirmBitLockerDestructiveOperation(part, LocExtension.Format("OpDeletePartition", part.PartitionNumber)))
            return;

        if (!_dialog.ConfirmWarning(
            LocExtension.Format("ConfirmDeletePrompt",
                part.PartitionNumber, diskNum, diskIdentity.ConfirmationSummary,
                part.LetterDisplay, part.SizeText, encryptionLine),
            LocExtension.Get("ConfirmDeleteTitle"))) return;

        var partNum = part.PartitionNumber;
        var driveLetter = part.DriveLetter;

        Queue.Enqueue(new PendingOperation
        {
            Type = PendingOperationType.Delete,
            Description = LocExtension.Format("QueueDescDelete", partNum, part.LetterDisplay, diskNum),
            DiskTarget = DiskTargetText(diskIdentity, LocExtension.Format("DiskTargetFallback", diskNum)),
            DiskIdentity = diskIdentity,
            ValidateTarget = BuildTargetValidator(diskIdentity),
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
        if (!await GuardRecoveryPartitionOperationAsync(part, "extend", "VerbExtend"))
            return;

        if (!GuardBitLockerMutation(part, LocExtension.Format("OpExtendPartition", part.PartitionNumber)))
            return;
        if (!GuardFilesystemCapability(FilesystemOperation.Extend, part.FileSystem, LocExtension.Format("OpExtendPartition", part.PartitionNumber)))
            return;

        // Warn about recovery / pagefile / system partitions
        var warnings = new List<string>();
        if (part.Type.Equals("Recovery", StringComparison.OrdinalIgnoreCase))
            warnings.Add(LocExtension.Get("ExtendWarningRecovery"));
        if (part.HasPagefile)
            warnings.Add(LocExtension.Get("ExtendWarningPagefile"));
        if (part.IsSystem)
            warnings.Add(LocExtension.Get("ExtendWarningSystem"));

        var msg = LocExtension.Format("ExtendPrompt",
            part.PartitionNumber, part.LetterDisplay, SelectedDisk.Number);
        if (warnings.Count > 0)
            msg += LocExtension.Format("ExtendWarningsHeading",
                string.Join("\n", warnings.Select(w => $"  - {w}")));

        if (!_dialog.Confirm(msg, LocExtension.Get("ConfirmExtendTitle"))) return;

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
            _dialog.ShowError(
                LocExtension.Format("ExtendFailed", ex.Message),
                LocExtension.Get("ExtendErrorTitle"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public static bool IsRecoveryPartition(PartitionInfo partition) =>
        partition.Type.Equals("Recovery", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="operation"/> is the English verb for the activity log, which stays English because it
    /// travels in support bundles; <paramref name="operationKey"/> names the translated verb for the dialog.
    /// </summary>
    private async Task<bool> GuardRecoveryPartitionOperationAsync(
        PartitionInfo partition, string operation, string operationKey)
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
            _log.Log($"Unable to read Windows RE status: {ex.Message}");
            reagentInfo = LocExtension.Format("ReagentUnavailable", ex.Message);
        }

        _dialog.ShowError(
            LocExtension.Format("RecoveryGuardBody", LocExtension.Get(operationKey), reagentInfo),
            LocExtension.Get("RecoveryGuardTitle"));
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
            LocExtension.Format("StoragePoolGuardBody",
                operation, SelectedDisk.ConfirmationSummary, SelectedDisk.StoragePoolName),
            LocExtension.Get("StoragePoolGuardTitle"));
    }

    private bool GuardFilesystemCapability(FilesystemOperation operation, string? fileSystem, string target)
    {
        var result = FilesystemCapabilityService.Evaluate(fileSystem, operation);
        if (result.IsAllowed)
            return true;

        _log.Log($"{target} blocked by filesystem policy: {result.Reason}");
        _dialog.ShowError(result.Reason, LocExtension.Get("FilesystemUnsupportedTitle"));
        return false;
    }

    private bool GuardUnsupportedType(PartitionInfo? partition, string operation)
    {
        if (partition is null || !partition.IsUnsupportedType)
            return true;

        if (!_dialog.ConfirmDanger(
            LocExtension.Format("UnsupportedTypeBody",
                operation, partition.Type, partition.PartitionDisplay, partition.SizeText),
            LocExtension.Format("UnsupportedTypeTitle", partition.Type)))
        {
            _log.Log($"{operation} cancelled - unsupported partition type: {partition.Type}");
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
            LocExtension.Get("BitLockerActiveTitle"));
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
            LocExtension.Get("BitLockerDataLossTitle"));
    }

    private async Task ExecuteSetActiveAsync()
    {
        if (SelectedPartition is null || SelectedDisk is null) return;

        var part = SelectedPartition;
        if (!_dialog.ConfirmWarning(
            LocExtension.Format("SetActivePrompt", part.PartitionNumber, SelectedDisk.Number),
            LocExtension.Get("ConfirmSetActiveTitle"))) return;

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
            _dialog.ShowError(
                LocExtension.Format("SetActiveFailed", ex.Message),
                LocExtension.Get("SetActiveErrorTitle"));
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

        // Separate keys per branch rather than a translated verb dropped into one sentence: word order and
        // capitalisation for "hide" and "unhide" do not line up across the shipped languages.
        if (!_dialog.Confirm(
            LocExtension.Format(willHide ? "HidePartitionPrompt" : "UnhidePartitionPrompt",
                part.PartitionNumber, part.LetterDisplay, SelectedDisk.Number),
            LocExtension.Get(willHide ? "ConfirmHideTitle" : "ConfirmUnhideTitle"))) return;

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
            _dialog.ShowError(
                LocExtension.Format(willHide ? "HidePartitionFailed" : "UnhidePartitionFailed", ex.Message),
                LocExtension.Get(willHide ? "HideErrorTitle" : "UnhideErrorTitle"));
        }
        finally
        {
            IsBusy = false;
        }
    }
}
