using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace PartitionPilot;

public partial class MainWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20h1 = 19;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeService.ThemeChanged += OnThemeChanged;
        ApplyTitleBarTheme();
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
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

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTitleBarTheme();

    private void ApplyTitleBarTheme()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var useDarkMode = ThemeService.IsDarkMode ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20h1, ref useDarkMode, sizeof(int));

        var captionColor = ThemeService.IsDarkMode ? ToColorRef(0x11, 0x13, 0x15) : ToColorRef(0xF0, 0xF2, 0xF5);
        var textColor = ThemeService.IsDarkMode ? ToColorRef(0xF4, 0xF7, 0xFA) : ToColorRef(0x1A, 0x1D, 0x21);
        DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionColor, sizeof(int));
        DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref textColor, sizeof(int));
    }

    private static int ToColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
