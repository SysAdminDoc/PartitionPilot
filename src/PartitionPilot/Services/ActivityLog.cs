using System.ComponentModel;
using System.IO;
using System.Text;

namespace PartitionPilot;

public class ActivityLog : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private readonly StringBuilder _sb = new();

    private static readonly string LogDir = Path.Combine(Path.GetTempPath(), "PartitionPilot");

    public string FullText => _sb.ToString();

    private void NotifyChanged() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FullText)));

    public void Log(string message)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        var line = $"[{stamp}] {message}\r\n";
        if (System.Windows.Application.Current?.Dispatcher is { } d && !d.CheckAccess())
            d.Invoke(() => { _sb.Append(line); NotifyChanged(); });
        else
        {
            _sb.Append(line);
            NotifyChanged();
        }
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
