using System.Windows;

namespace PartitionPilot.Dialogs;

public partial class ResizePartitionDialog : Window
{
    public long NewSizeBytes { get; private set; }
    private readonly long _min, _max;

    public ResizePartitionDialog(char driveLetter, long currentBytes, long minBytes, long maxBytes)
    {
        InitializeComponent();
        _min = minBytes;
        _max = maxBytes;
        var curGB = Math.Round(currentBytes / (double)(1L << 30), 2);
        var minGB = Math.Round(minBytes / (double)(1L << 30), 2);
        var maxGB = Math.Round(maxBytes / (double)(1L << 30), 2);
        txtInfo.Text = $"{driveLetter}: — {curGB} GB  (min: {minGB} GB, max: {maxGB} GB)";
        txtSize.Text = curGB.ToString("F2");
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(txtSize.Text, out var sizeGB) || sizeGB <= 0)
        {
            MessageBox.Show("Enter a valid size.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        NewSizeBytes = (long)Math.Round(sizeGB * (1L << 30));
        NewSizeBytes = Math.Max(NewSizeBytes, _min);
        NewSizeBytes = Math.Min(NewSizeBytes, _max);
        DialogResult = true;
    }
}
