using System.Windows;

namespace PartitionPilot.Dialogs;

public partial class CreatePartitionDialog : Window
{
    public double SizeGB { get; private set; }
    public char SelectedLetter { get; private set; }
    public string FileSystem { get; private set; } = "NTFS";
    public string VolumeLabel { get; private set; } = "";
    public bool QuickFormat { get; private set; } = true;

    public CreatePartitionDialog(double availableGB, IEnumerable<char> availableLetters)
    {
        InitializeComponent();
        txtInfo.Text = $"{availableGB} GB";
        txtSize.Text = availableGB.ToString("F2");
        foreach (var l in availableLetters) cmbLetter.Items.Add(l);
        if (cmbLetter.Items.Count > 0) cmbLetter.SelectedIndex = 0;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(txtSize.Text, out var size) || size <= 0)
        {
            MessageBox.Show("Enter a valid size.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (cmbLetter.SelectedItem is not char letter)
        {
            MessageBox.Show("Select a drive letter.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SizeGB = size;
        SelectedLetter = letter;
        FileSystem = (cmbFS.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "NTFS";
        VolumeLabel = txtLabel.Text.Trim();
        QuickFormat = chkQuick.IsChecked == true;
        DialogResult = true;
    }
}
