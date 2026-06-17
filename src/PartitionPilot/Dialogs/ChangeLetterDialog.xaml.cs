using System.Windows;

namespace PartitionPilot.Dialogs;

public partial class ChangeLetterDialog : Window
{
    private readonly IDialogService _dialog;

    public char NewLetter { get; private set; }

    public ChangeLetterDialog(char? currentLetter, IEnumerable<char> availableLetters)
        : this(currentLetter, availableLetters, new MessageBoxDialogService())
    {
    }

    internal ChangeLetterDialog(char? currentLetter, IEnumerable<char> availableLetters, IDialogService dialog)
    {
        _dialog = dialog;
        InitializeComponent();
        txtCurrent.Text = currentLetter.HasValue ? $"{currentLetter}:" : "(none)";
        foreach (var l in availableLetters) cmbLetter.Items.Add(l);
        if (cmbLetter.Items.Count > 0) cmbLetter.SelectedIndex = 0;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (cmbLetter.SelectedItem is not char letter)
        {
            _dialog.ShowWarning("Select an available drive letter before continuing.", "Drive Letter Required");
            return;
        }
        NewLetter = letter;
        DialogResult = true;
    }
}
