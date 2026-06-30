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
        var preset = (cmbPreset.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        AllocationUnitSize = null;

        if (preset == "Camera")
        {
            SelectFileSystem("FAT32");
            AllocationUnitSize = "32768";
            txtLabel.Text = "";
        }
        else if (preset == "Nintendo")
        {
            SelectFileSystem("FAT32");
            AllocationUnitSize = "65536";
            txtLabel.Text = "";
        }
        else if (preset == "Raspberry")
        {
            SelectFileSystem("FAT32");
            txtLabel.Text = "boot";
        }
        else if (preset == "LargeUsb")
        {
            SelectFileSystem("exFAT");
            txtLabel.Text = "";
        }
        else if (preset == "GeneralNtfs")
        {
            SelectFileSystem("NTFS");
            txtLabel.Text = "";
        }
    }

    private void SelectFileSystem(string fs)
    {
        foreach (ComboBoxItem item in cmbFS.Items)
        {
            if (item.Tag?.ToString() == fs)
            {
                cmbFS.SelectedItem = item;
                return;
            }
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        FileSystem = (cmbFS.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "NTFS";
        VolumeLabel = txtLabel.Text.Trim();
        QuickFormat = chkQuick.IsChecked == true;
        DialogResult = true;
    }
}
