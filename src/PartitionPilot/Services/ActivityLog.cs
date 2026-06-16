using System.ComponentModel;

namespace PartitionPilot;

public class ActivityLog : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string _fullText = "";

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
}
