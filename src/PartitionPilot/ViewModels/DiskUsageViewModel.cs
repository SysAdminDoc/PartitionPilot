using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PartitionPilot.Controls;

namespace PartitionPilot;

public class DiskUsageViewModel : ViewModelBase
{
    private readonly WmiDiskService _wmiService;
    private readonly ActivityLog _log;

    public ObservableCollection<char> DriveLetters { get; } = new();
    public ObservableCollection<FolderSizeInfo> TopFolders { get; } = new();

    private IReadOnlyList<TreemapItem>? _treemapItems;
    public IReadOnlyList<TreemapItem>? TreemapItems
    {
        get => _treemapItems;
        set => SetProperty(ref _treemapItems, value);
    }

    private char _selectedDrive;
    public char SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (SetProperty(ref _selectedDrive, value))
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

    private string _summaryText = "";
    public string SummaryText
    {
        get => _summaryText;
        set => SetProperty(ref _summaryText, value);
    }

    private CancellationTokenSource? _cts;

    public ICommand ScanCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RefreshCommand { get; }

    public DiskUsageViewModel(WmiDiskService wmiService, ActivityLog log)
    {
        _wmiService = wmiService;
        _log = log;

        ScanCommand = new AsyncRelayCommand(_ => ScanAsync(), _ => SelectedDrive != default);
        CancelCommand = new WpfRelayCommand(_ => _cts?.Cancel(), _ => IsBusy);
        RefreshCommand = new AsyncRelayCommand(_ => RefreshDrivesAsync());
    }

    public async Task RefreshDrivesAsync()
    {
        var volumes = await _wmiService.GetVolumesAsync();
        var letters = volumes
            .Where(v => v.DriveLetter.HasValue)
            .Select(v => v.DriveLetter!.Value)
            .OrderBy(c => c)
            .ToList();

        Application.Current.Dispatcher.Invoke(() =>
        {
            DriveLetters.Clear();
            foreach (var l in letters)
                DriveLetters.Add(l);

            if (!letters.Contains(SelectedDrive))
                SelectedDrive = letters.FirstOrDefault();
        });
    }

    private async Task ScanAsync()
    {
        if (SelectedDrive == default) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsBusy = true;
        StatusText = $"Scanning {SelectedDrive}:\\...";
        SummaryText = "";
        var clearStatusWhenDone = true;

        try
        {
            _log.Log($"Starting disk usage scan on {SelectedDrive}:\\...");
            var rootPath = $"{SelectedDrive}:\\";

            var scanSw = System.Diagnostics.Stopwatch.StartNew();
            var results = await Task.Run(() => ScanTopFolders(rootPath, ct), ct);
            scanSw.Stop();

            long totalScanned = results.Sum(f => f.Size);
            foreach (var f in results)
                f.Proportion = totalScanned > 0 ? (double)f.Size / totalScanned : 0;

            Application.Current.Dispatcher.Invoke(() =>
            {
                TopFolders.Clear();
                foreach (var f in results)
                    TopFolders.Add(f);
            });

            TreemapItems = results
                .Where(f => f.Size > 0)
                .Select(f => new TreemapItem { Label = f.Name, Size = f.Size, Path = f.Path })
                .ToList();

            SummaryText = $"Scanned {results.Count} top-level folders. Total: {SizeUtil.Format(totalScanned)} in {scanSw.Elapsed.TotalSeconds:F1}s";
            _log.Log($"Disk usage scan complete on {SelectedDrive}:\\. {results.Count} folders, {SizeUtil.Format(totalScanned)} total in {scanSw.Elapsed.TotalSeconds:F1}s.");
        }
        catch (OperationCanceledException)
        {
            _log.Log("Disk usage scan cancelled.");
            StatusText = "Scan cancelled.";
            SummaryText = "Disk usage scan cancelled.";
            clearStatusWhenDone = false;
        }
        catch (Exception ex)
        {
            _log.Log($"Disk usage scan failed: {ex.Message}");
            StatusText = $"Scan failed: {ex.Message}";
            SummaryText = $"Scan failed: {ex.Message}";
            clearStatusWhenDone = false;
        }
        finally
        {
            IsBusy = false;
            if (clearStatusWhenDone)
                StatusText = "";
        }
    }

    private static List<FolderSizeInfo> ScanTopFolders(string rootPath, CancellationToken ct)
    {
        var results = new List<FolderSizeInfo>();
        DirectoryInfo root;

        try { root = new DirectoryInfo(rootPath); }
        catch { return results; }

        DirectoryInfo[] topDirs;
        try { topDirs = root.GetDirectories(); }
        catch { return results; }

        foreach (var dir in topDirs)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var (size, count) = MeasureDirectory(dir, ct);
                if (size > 0)
                {
                    results.Add(new FolderSizeInfo
                    {
                        Path = dir.FullName,
                        Name = dir.Name,
                        Size = size,
                        FileCount = count
                    });
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* access denied or other IO error */ }
        }

        // Also measure loose files in root
        try
        {
            long rootFileSize = 0;
            int rootFileCount = 0;
            foreach (var file in root.EnumerateFiles())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    rootFileSize += file.Length;
                    rootFileCount++;
                }
                catch { /* access denied */ }
            }

            if (rootFileSize > 0)
            {
                results.Add(new FolderSizeInfo
                {
                    Path = rootPath,
                    Name = "(root files)",
                    Size = rootFileSize,
                    FileCount = rootFileCount
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* ignore */ }

        results.Sort((a, b) => b.Size.CompareTo(a.Size));
        return results.Take(30).ToList();
    }

    private static (long size, int count) MeasureDirectory(DirectoryInfo dir, CancellationToken ct)
    {
        long totalSize = 0;
        int totalCount = 0;

        try
        {
            foreach (var file in dir.EnumerateFiles("*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            }))
            {
                if (totalCount % 1000 == 0)
                    ct.ThrowIfCancellationRequested();

                try
                {
                    totalSize += file.Length;
                    totalCount++;
                }
                catch { /* access denied */ }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* access denied at directory level */ }

        return (totalSize, totalCount);
    }
}
