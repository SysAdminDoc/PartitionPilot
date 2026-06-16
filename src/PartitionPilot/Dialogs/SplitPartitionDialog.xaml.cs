using System.Windows;

namespace PartitionPilot.Dialogs;

public partial class SplitPartitionDialog : Window
{
    public double NewPartSizeGB { get; private set; }
    public char NewLetter { get; private set; }
    public string FileSystem { get; private set; } = "NTFS";
    public string VolumeLabel { get; private set; } = "";

    public SplitPartitionDialog(char driveLetter, long currentBytes, long minKeepBytes, double maxNewGB, IEnumerable<char> availableLetters)
    {
        InitializeComponent();
        var curGB = Math.Round(currentBytes / (double)(1L << 30), 2);
        var minKeepGB = Math.Ceiling(minKeepBytes / (double)(1L << 30));
        txtInfo.Text = $"{driveLetter}: — {curGB} GB  (min keep: {minKeepGB} GB, max new: {maxNewGB} GB)";
        txtNewSize.Text = Math.Floor(maxNewGB / 2).ToString("F0");
        foreach (var l in availableLetters) cmbLetter.Items.Add(l);
        if (cmbLetter.Items.Count > 0) cmbLetter.SelectedIndex = 0;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(txtNewSize.Text, out var size) || size <= 0)
        {
            MessageBox.Show("Enter a valid size.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (cmbLetter.SelectedItem is not char letter)
        {
            MessageBox.Show("Select a drive letter.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        NewPartSizeGB = size;
        NewLetter = letter;
        FileSystem = (cmbFS.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "NTFS";
        VolumeLabel = txtLabel.Text.Trim();
        DialogResult = true;
    }
}
