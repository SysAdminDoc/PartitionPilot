using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Input;

namespace PartitionPilot;

public partial class MainViewModel : ViewModelBase
{
    private readonly ProcessRunner _processRunner;
    private readonly IWmiDiskService _wmiService;
    private readonly IDialogService _dialog;

    public ActivityLog Log { get; }
    public PartitionsViewModel Partitions { get; }
    public SnapshotBrowserViewModel Snapshots { get; }
    public DiskHealthViewModel DiskHealth { get; }
    public ToolsViewModel Tools { get; }
    public DiskImagesViewModel DiskImages { get; }
    public DiskUsageViewModel DiskUsage { get; }
    public DiskCloningViewModel DiskCloning { get; }
    public HexViewerViewModel HexViewer { get; }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set
        {
            if (SetProperty(ref _statusText, value))
                OnPropertyChanged(nameof(SessionStateDetail));
        }
    }

    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetProperty(ref _selectedTabIndex, value))
                TabChangedCommand.Execute(value);
        }
    }

    public ICommand TabChangedCommand { get; }
    public ICommand ExportLogCommand { get; }
    public ICommand ExportSupportBundleCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand RefreshCurrentCommand { get; }
    public ICommand ShowFilesystemSupportCommand { get; }

    public string VersionText => GetVersionText();
    public string AdminSessionText { get; }
    public string AdminSessionDetail { get; }
    public string ElevationContextText { get; }
    public bool IsElevated { get; }
    public bool IsReadOnly => !IsElevated;
    public ICommand ElevateCommand { get; }
    public string SessionStateText => "Session state";
    public string SessionStateDetail => StatusText;

    private string _themeLabel = ThemeService.GetLabel();
    public string ThemeLabel
    {
        get => _themeLabel;
        set => SetProperty(ref _themeLabel, value);
    }

    public MainViewModel()
    {
        _processRunner = new ProcessRunner();
        Log = new ActivityLog();
        _wmiService = App.IsSimulationMode
            ? new SimulatedDiskService()
            : new WmiDiskService(_processRunner, Log);
        _dialog = new MessageBoxDialogService();

        Partitions = new PartitionsViewModel(_wmiService, _processRunner, Log, _dialog);
        Snapshots = new SnapshotBrowserViewModel(new PartitionTableBackup(_wmiService, Log), Log, _dialog);
        DiskHealth = new DiskHealthViewModel(_wmiService, _processRunner, Log);
        Tools = new ToolsViewModel(_wmiService, _processRunner, Log, _dialog);
        DiskImages = new DiskImagesViewModel(_processRunner, _wmiService, Log, _dialog);
        DiskUsage = new DiskUsageViewModel(_wmiService, Log);
        DiskCloning = new DiskCloningViewModel(_processRunner, _wmiService, Log, _dialog);
        HexViewer = new HexViewerViewModel(_wmiService, Log);

        TabChangedCommand = new AsyncRelayCommand(OnTabChangedAsync);
        ExportLogCommand = new WpfRelayCommand(_ => ExportLog());
        ExportSupportBundleCommand = new AsyncRelayCommand(_ => ExportSupportBundleAsync());
        ToggleThemeCommand = new WpfRelayCommand(_ => ToggleTheme());
        RefreshCurrentCommand = new AsyncRelayCommand(_ => RefreshCurrentAsync());
        ShowFilesystemSupportCommand = new WpfRelayCommand(_ => ShowFilesystemSupport());

        var isAdmin = IsRunningAsAdministrator();
        IsElevated = isAdmin;
        AdminSessionText = isAdmin ? "Admin session" : "Read-only session";
        AdminSessionDetail = isAdmin ? "Disk changes available" : "Run as administrator for write operations";
        ElevationContextText = DetectElevationContext(isAdmin);
        ElevateCommand = new WpfRelayCommand(_ => RelaunchElevated(), _ => !IsElevated);

        Log.Log("PartitionPilot ready.");
        _ = CheckForUpdateAsync();
        _ = Partitions.LoadDisksAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            var veloUpdate = await UpdateService.CheckForVelopackUpdateAsync(Log);
            if (veloUpdate is not null)
            {
                StatusText = $"Update available: v{veloUpdate.TargetFullRelease.Version}";
                try
                {
                    await UpdateService.DownloadAndApplyAsync(veloUpdate, Log);
                    StatusText = $"Update v{veloUpdate.TargetFullRelease.Version} ready — restart to apply";
                }
                catch
                {
                    StatusText = $"Update v{veloUpdate.TargetFullRelease.Version} available (download failed)";
                }
                return;
            }

            var result = await UpdateService.CheckForUpdateAsync();
            if (result is { available: true } update)
            {
                Log.Log($"Update available: v{update.version} - {update.url} ({update.verificationStatus}: {update.verificationDetail})");
                StatusText = $"Update available: v{update.version} ({update.verificationStatus})";
            }
        }
        catch (Exception ex)
        {
            Log.Log($"Update check failed: {ex.Message}");
        }
    }

    private void ToggleTheme()
    {
        ThemeService.CycleTheme();
        ThemeLabel = ThemeService.GetLabel();
        var modeName = ThemeService.Preference.ToString().ToLowerInvariant();
        Log.Log($"Theme applied: {modeName} mode.");
        StatusText = $"{ThemeService.Preference} theme applied";
    }

    private void ShowFilesystemSupport()
    {
        var dialog = new Dialogs.FilesystemSupportDialog();
        dialog.Owner = Application.Current.MainWindow;
        dialog.ShowDialog();
    }

    private void ExportLog()
    {
        try
        {
            var path = Log.Export();
            Log.Log($"Log exported to: {path}");
            _dialog.ShowInfo($"Log exported to:\n{path}", "Export Complete");
        }
        catch (Exception ex)
        {
            Log.Log($"Log export failed: {ex.Message}");
            _dialog.ShowError($"Failed to export log:\n{ex.Message}", "Export Error");
        }
    }

    public void OnClosing()
    {
        Log.AutoSave();
    }

    private async Task RefreshCurrentAsync()
    {
        try
        {
            StatusText = "Refreshing current workspace...";
            await RefreshTabAsync(SelectedTabIndex);
            StatusText = "Ready";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            Log.Log($"Refresh error: {ex.Message}");
        }
    }

    private async Task OnTabChangedAsync(object? parameter)
    {
        try
        {
            var index = parameter is int i ? i : _selectedTabIndex;
            await RefreshTabAsync(index);
            StatusText = "Ready";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            Log.Log($"Tab switch error: {ex.Message}");
        }
    }

    private async Task RefreshTabAsync(int index)
    {
        switch (index)
        {
            case 0:
                StatusText = "Loading partition layout...";
                await Partitions.LoadDisksAsync();
                break;
            case 1:
                StatusText = "Loading partition snapshots...";
                await Snapshots.RefreshAsync();
                break;
            case 2:
                StatusText = "Loading disk health data...";
                await DiskHealth.RefreshAsync();
                break;
            case 3:
                StatusText = "Loading tools drive lists...";
                await Tools.RefreshDriveListsAsync();
                break;
            case 4:
                StatusText = "Loading disk images...";
                await DiskImages.RefreshAsync();
                break;
            case 5:
                StatusText = "Loading drive list...";
                await DiskUsage.RefreshDrivesAsync();
                break;
            case 6:
                StatusText = "Loading cloning data...";
                await DiskCloning.RefreshAsync();
                break;
            case 7:
                StatusText = "Loading hex viewer...";
                await HexViewer.RefreshAsync();
                break;
        }
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private async Task ExportSupportBundleAsync()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Support Bundle",
            Filter = "ZIP Archive (*.zip)|*.zip",
            FileName = $"PartitionPilot-support-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            DefaultExt = ".zip"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            StatusText = "Generating support bundle...";
            Log.Log("Generating support bundle...");

            await SupportBundleService.CreateAsync(
                new SupportBundleOptions(
                    dlg.FileName,
                    GetVersionText(),
                    ElevationContextText,
                    Log.FullText,
                    PartitionTableBackup.BackupDirectory,
                    IsRunningAsAdministrator(),
                    DateTimeOffset.Now),
                _wmiService);

            Log.Log($"Support bundle exported to: {dlg.FileName}");
            _dialog.ShowInfo($"Support bundle exported to:\n{dlg.FileName}\n\nSerial numbers and user paths have been redacted.",
                "Support Bundle Exported");

            StatusText = "Ready";
        }
        catch (Exception ex)
        {
            Log.Log($"Support bundle export failed: {ex.Message}");
            _dialog.ShowError($"Failed to export support bundle:\n{ex.Message}", "Export Error");
            StatusText = "Ready";
        }
    }

    public static string RedactSupportBundleText(string text)
    {
        return SupportBundleService.RedactText(text);
    }

    private static void RelaunchElevated()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            };
            System.Diagnostics.Process.Start(psi);
            System.Windows.Application.Current.Shutdown();
        }
        catch { }
    }

    private static string DetectElevationContext(bool isAdmin)
    {
        if (!isAdmin) return "Standard (unelevated)";

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var isAdminProtection = userProfile.Contains("ADMIN_", StringComparison.OrdinalIgnoreCase);
        return isAdminProtection
            ? "Administrator Protection (SMAA profile)"
            : "Legacy UAC (elevated)";
    }

    public static string GetVersionText() => $"PartitionPilot v{UpdateService.GetCurrentVersion()}";
}
