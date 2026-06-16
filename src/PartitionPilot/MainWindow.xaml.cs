using System.Windows;
using System.Windows.Controls;

namespace PartitionPilot;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.ScrollToEnd();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.OnClosing();
    }
}
