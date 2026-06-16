using System.Windows;

namespace PartitionPilot.Dialogs;

public partial class ChangeLetterDialog : Window
{
    public char NewLetter { get; private set; }

    public ChangeLetterDialog(char? currentLetter, IEnumerable<char> availableLetters)
    {
        InitializeComponent();
        txtCurrent.Text = currentLetter.HasValue ? $"{currentLetter}:" : "(none)";
        foreach (var l in availableLetters) cmbLetter.Items.Add(l);
        if (cmbLetter.Items.Count > 0) cmbLetter.SelectedIndex = 0;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (cmbLetter.SelectedItem is not char letter)
        {
            MessageBox.Show("Select a letter.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        NewLetter = letter;
        DialogResult = true;
    }
}
