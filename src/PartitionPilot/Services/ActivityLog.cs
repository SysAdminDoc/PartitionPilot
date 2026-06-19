using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows.Data;
using System.Windows.Input;

namespace PartitionPilot;

public class ActivityLog : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private readonly StringBuilder _sb = new();
    private const string AllFilter = "All";

    private static readonly string LogDir = ResolveLogDir();

    private static string ResolveLogDir()
    {
        var exeDir = AppContext.BaseDirectory;
        var portableMarker = Path.Combine(exeDir, "portable.txt");
        if (File.Exists(portableMarker))
            return Path.Combine(exeDir, "logs");
        var programData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PartitionPilot", "logs");
        try
        {
            Directory.CreateDirectory(programData);
            return programData;
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), "PartitionPilot");
        }
    }

    public ObservableCollection<LogEntry> Entries { get; } = new();
    public ICollectionView FilteredEntries { get; }
    public ICommand SetFilterCommand { get; }
    public ICommand ClearCommand { get; }

    public string FullText => _sb.ToString();
    public string EntryCountText => Entries.Count == 1 ? "1 entry" : $"{Entries.Count} entries";

    private string _selectedLevelFilter = AllFilter;
    public string SelectedLevelFilter
    {
        get => _selectedLevelFilter;
        set
        {
            if (_selectedLevelFilter != value)
            {
                _selectedLevelFilter = value;
                FilteredEntries.Refresh();
                NotifyChanged(nameof(SelectedLevelFilter));
            }

            NotifyChanged(nameof(IsAllFilter));
            NotifyChanged(nameof(IsInfoFilter));
            NotifyChanged(nameof(IsWarningFilter));
            NotifyChanged(nameof(IsErrorFilter));
        }
    }

    public bool IsAllFilter => SelectedLevelFilter == AllFilter;
    public bool IsInfoFilter => SelectedLevelFilter == "Info";
    public bool IsWarningFilter => SelectedLevelFilter == "Warning";
    public bool IsErrorFilter => SelectedLevelFilter == "Error";

    public ActivityLog()
    {
        FilteredEntries = CollectionViewSource.GetDefaultView(Entries);
        FilteredEntries.Filter = ShouldShowEntry;
        SetFilterCommand = new WpfRelayCommand(parameter =>
        {
            if (parameter is string level)
                SelectedLevelFilter = level;
        });
        ClearCommand = new WpfRelayCommand(_ => Clear());
    }

    private void NotifyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Log(string message)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        var line = $"[{stamp}] {message}\r\n";
        var entry = new LogEntry
        {
            Timestamp = stamp,
            Level = InferLevel(message),
            Message = message
        };

        if (System.Windows.Application.Current?.Dispatcher is { } d && !d.CheckAccess())
        {
            d.Invoke(() => Append(line, entry));
        }
        else
        {
            Append(line, entry);
        }
    }

    public void Clear()
    {
        if (System.Windows.Application.Current?.Dispatcher is { } d && !d.CheckAccess())
        {
            d.Invoke(ClearCore);
        }
        else
        {
            ClearCore();
        }
    }

    private void Append(string line, LogEntry entry)
    {
        _sb.Append(line);
        Entries.Insert(0, entry);
        NotifyChanged(nameof(FullText));
        NotifyChanged(nameof(EntryCountText));
    }

    private void ClearCore()
    {
        _sb.Clear();
        Entries.Clear();
        NotifyChanged(nameof(FullText));
        NotifyChanged(nameof(EntryCountText));
    }

    private bool ShouldShowEntry(object item) =>
        item is LogEntry entry && (SelectedLevelFilter == AllFilter || entry.Level == SelectedLevelFilter);

    private static string InferLevel(string message)
    {
        if (message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("exception", StringComparison.OrdinalIgnoreCase))
            return "Error";

        if (message.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("warn", StringComparison.OrdinalIgnoreCase))
            return "Warning";

        return "Info";
    }

    public string Export()
    {
        Directory.CreateDirectory(LogDir);
        var fileName = $"PartitionPilot_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        var path = Path.Combine(LogDir, fileName);
        File.WriteAllText(path, _sb.ToString());
        return path;
    }

    public void AutoSave()
    {
        if (_sb.Length == 0) return;
        try
        {
            Directory.CreateDirectory(LogDir);
            var path = Path.Combine(LogDir, "session_latest.log");
            File.WriteAllText(path, _sb.ToString());
        }
        catch { /* best-effort */ }
    }
}
