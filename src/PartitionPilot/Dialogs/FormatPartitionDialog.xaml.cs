using System.Windows;

namespace PartitionPilot.Dialogs;

public partial class FormatPartitionDialog : Window
{
    public string FileSystem { get; private set; } = "NTFS";
    public string VolumeLabel { get; private set; } = "";
    public bool QuickFormat { get; private set; } = true;

    public FormatPartitionDialog(char driveLetter, string currentFs, long sizeBytes)
    {
        InitializeComponent();
        txtInfo.Text = $"{driveLetter}: — {currentFs}, {SizeUtil.Format(sizeBytes)}";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        FileSystem = (cmbFS.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "NTFS";
        VolumeLabel = txtLabel.Text.Trim();
        QuickFormat = chkQuick.IsChecked == true;
        DialogResult = true;
    }
}
