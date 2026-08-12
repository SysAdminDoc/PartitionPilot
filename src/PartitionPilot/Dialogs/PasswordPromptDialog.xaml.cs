using System.Windows;

namespace PartitionPilot.Dialogs;

public partial class PasswordPromptDialog : Window
{
    public string Password => PasswordInput.Password;

    public PasswordPromptDialog(string message, string title)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = message;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
