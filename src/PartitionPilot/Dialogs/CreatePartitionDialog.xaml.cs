using System.Windows;

namespace PartitionPilot.Dialogs;

public partial class CreatePartitionDialog : Window
{
    private readonly IDialogService _dialog;

    public double SizeGB { get; private set; }
    public char SelectedLetter { get; private set; }
    public string FileSystem { get; private set; } = "NTFS";
    public string VolumeLabel { get; private set; } = "";
    public bool QuickFormat { get; private set; } = true;

    public CreatePartitionDialog(double availableGB, IEnumerable<char> availableLetters)
        : this(availableGB, availableLetters, new MessageBoxDialogService())
    {
    }

    internal CreatePartitionDialog(double availableGB, IEnumerable<char> availableLetters, IDialogService dialog)
    {
        _dialog = dialog;
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
            _dialog.ShowWarning(LocExtension.Get("DialogPartitionSizeRequired"), LocExtension.Get("DialogSizeRequired"));
            return;
        }
        if (cmbLetter.SelectedItem is not char letter)
        {
            _dialog.ShowWarning(LocExtension.Get("DialogSelectDriveLetterRequired"), LocExtension.Get("DialogDriveLetterRequired"));
            return;
        }
        SizeGB = size;
        SelectedLetter = letter;
        FileSystem = (cmbFS.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "NTFS";
        VolumeLabel = txtLabel.Text.Trim();
        QuickFormat = chkQuick.IsChecked == true;
        DialogResult = true;
    }
}
