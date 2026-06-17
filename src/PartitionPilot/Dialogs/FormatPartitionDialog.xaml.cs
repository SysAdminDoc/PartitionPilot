using System.Windows;
using System.Windows.Controls;

namespace PartitionPilot.Dialogs;

public partial class FormatPartitionDialog : Window
{
    public string FileSystem { get; private set; } = "NTFS";
    public string VolumeLabel { get; private set; } = "";
    public bool QuickFormat { get; private set; } = true;
    public string? AllocationUnitSize { get; private set; }

    public FormatPartitionDialog(char driveLetter, string currentFs, long sizeBytes)
    {
        InitializeComponent();
        txtInfo.Text = $"{driveLetter}: — {currentFs}, {SizeUtil.Format(sizeBytes)}";
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbFS is null) return;
        var preset = (cmbPreset.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        AllocationUnitSize = null;

        if (preset.StartsWith("Camera"))
        {
            SelectFileSystem("FAT32");
            AllocationUnitSize = "32768";
            txtLabel.Text = "";
        }
        else if (preset.StartsWith("Nintendo"))
        {
            SelectFileSystem("FAT32");
            AllocationUnitSize = "65536";
            txtLabel.Text = "";
        }
        else if (preset.StartsWith("Raspberry"))
        {
            SelectFileSystem("FAT32");
            txtLabel.Text = "boot";
        }
        else if (preset.StartsWith("Large USB"))
        {
            SelectFileSystem("exFAT");
            txtLabel.Text = "";
        }
        else if (preset.StartsWith("General NTFS"))
        {
            SelectFileSystem("NTFS");
            txtLabel.Text = "";
        }
    }

    private void SelectFileSystem(string fs)
    {
        foreach (ComboBoxItem item in cmbFS.Items)
        {
            if (item.Content?.ToString() == fs)
            {
                cmbFS.SelectedItem = item;
                return;
            }
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        FileSystem = (cmbFS.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "NTFS";
        VolumeLabel = txtLabel.Text.Trim();
        QuickFormat = chkQuick.IsChecked == true;
        DialogResult = true;
    }
}
