using System.ComponentModel;
using System.IO;

namespace PartitionPilot;

public class ActivityLog : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string _fullText = "";

    private static readonly string LogDir = Path.Combine(Path.GetTempPath(), "PartitionPilot");

    public string FullText
    {
        get => _fullText;
        private set { _fullText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FullText))); }
    }

    public void Log(string message)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        var line = $"[{stamp}] {message}\r\n";
        if (System.Windows.Application.Current?.Dispatcher is { } d && !d.CheckAccess())
            d.Invoke(() => FullText += line);
        else
            FullText += line;
    }

    public string Export()
    {
        Directory.CreateDirectory(LogDir);
        var fileName = $"PartitionPilot_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        var path = Path.Combine(LogDir, fileName);
        File.WriteAllText(path, FullText);
        return path;
    }

    public void AutoSave()
    {
        if (string.IsNullOrWhiteSpace(FullText)) return;
        try
        {
            Directory.CreateDirectory(LogDir);
            var path = Path.Combine(LogDir, "session_latest.log");
            File.WriteAllText(path, FullText);
        }
        catch { /* best-effort */ }
    }
}
