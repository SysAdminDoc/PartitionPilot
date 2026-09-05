using System.Windows;
using System.Windows.Automation;
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

    private const double DefaultActivityLogHeight = 184;
    private double _restoredActivityLogHeight = DefaultActivityLogHeight;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        RestoreShellLayout(ShellSettingsService.Load());
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeService.ThemeChanged += OnThemeChanged;
        ApplyTitleBarTheme();
    }

    private void RestoreShellLayout(ShellSettings settings)
    {
        if (ShellSettingsService.TryGetVisiblePlacement(
                settings, SystemParameters.WorkArea, out var left, out var top, out var width, out var height))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        if (settings.WindowMaximized)
            WindowState = WindowState.Maximized;

        if (settings.ActivityLogHeight is > 0)
            _restoredActivityLogHeight = settings.ActivityLogHeight.Value;

        SetActivityLogCollapsed(settings.ActivityLogCollapsed);

        if (DataContext is MainViewModel vm && settings.SelectedTabIndex >= 0)
            vm.SelectedTabIndex = settings.SelectedTabIndex;
    }

    private void OnToggleActivityLog(object sender, RoutedEventArgs e) =>
        SetActivityLogCollapsed(rowActivityLog.Height.Value > 0);

    private void SetActivityLogCollapsed(bool collapsed)
    {
        if (collapsed)
        {
            if (rowActivityLog.Height.Value > 0)
                _restoredActivityLogHeight = rowActivityLog.Height.Value;

            rowActivityLog.Height = new GridLength(0);
            logSplitter.Visibility = Visibility.Collapsed;
            btnToggleLog.Content = LocExtension.Get("ExpandActivityLog");
            AutomationProperties.SetName(btnToggleLog, LocExtension.Get("ExpandActivityLogAutomationName"));
        }
        else
        {
            rowActivityLog.Height = new GridLength(
                _restoredActivityLogHeight > 0 ? _restoredActivityLogHeight : DefaultActivityLogHeight);
            logSplitter.Visibility = Visibility.Visible;
            btnToggleLog.Content = LocExtension.Get("CollapseActivityLog");
            AutomationProperties.SetName(btnToggleLog, LocExtension.Get("CollapseActivityLogAutomationName"));
        }
    }

    private void SaveShellLayout()
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        var collapsed = rowActivityLog.Height.Value <= 0;

        ShellSettingsService.Save(new ShellSettings
        {
            WindowLeft = bounds.Left,
            WindowTop = bounds.Top,
            WindowWidth = bounds.Width,
            WindowHeight = bounds.Height,
            WindowMaximized = WindowState == WindowState.Maximized,
            SelectedTabIndex = (DataContext as MainViewModel)?.SelectedTabIndex ?? 0,
            ActivityLogHeight = collapsed ? _restoredActivityLogHeight : rowActivityLog.Height.Value,
            ActivityLogCollapsed = collapsed
        }, (DataContext as MainViewModel)?.Log);
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveShellLayout();
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
