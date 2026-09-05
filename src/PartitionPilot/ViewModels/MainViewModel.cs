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

    private string _statusText = LocExtension.Get("Ready");
    public string StatusText
    {
        get => _statusText;
        set
        {
            if (SetProperty(ref _statusText, value))
                OnPropertyChanged(nameof(SessionStateDetail));
        }
    }

    private StatusSeverity _statusSeverity = StatusSeverity.Normal;

    /// <summary>
    /// Drives the status indicator. It used to be hardcoded green, so the shell showed a healthy dot
    /// while the status text next to it reported a failure.
    /// </summary>
    public StatusSeverity StatusSeverity
    {
        get => _statusSeverity;
        set
        {
            if (SetProperty(ref _statusSeverity, value))
                OnPropertyChanged(nameof(StatusSeverityText));
        }
    }

    /// <summary>Spoken by a screen reader, so the severity is not conveyed by colour alone.</summary>
    public string StatusSeverityText => StatusSeverity switch
    {
        StatusSeverity.Error => LocExtension.Get("Error"),
        StatusSeverity.Warning => LocExtension.Get("Warning"),
        _ => LocExtension.Get("OK")
    };

    private void SetStatus(string text, StatusSeverity severity = StatusSeverity.Normal)
    {
        StatusText = text;
        StatusSeverity = severity;
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

    // Resolved on each read rather than captured in the constructor, so a language change refreshes them
    // the same way the XAML-declared labels refresh.
    public string AdminSessionText => LocExtension.Get(IsElevated ? "AdminSession" : "ReadOnlySession");
    public string AdminSessionDetail => LocExtension.Get(IsElevated ? "AdminSessionDetail" : "ReadOnlySessionDetail");
    public string ElevationContextText => LocExtension.Get(DetectElevationContextKey(IsElevated));
    public bool IsElevated { get; }
    public bool IsReadOnly => !IsElevated;
    public ICommand ElevateCommand { get; }
    public string SessionStateText => LocExtension.Get("SessionStateLabel");
    public string SessionStateDetail => StatusText;

    /// <summary>Every label this view model resolves from resources rather than from bound state.</summary>
    private static readonly string[] LocalizedLabels =
    [
        nameof(AdminSessionText), nameof(AdminSessionDetail), nameof(ElevationContextText),
        nameof(SessionStateText), nameof(ThemeLabel)
    ];

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
        Snapshots = new SnapshotBrowserViewModel(
            new PartitionTableBackup(_wmiService, Log), Log, _dialog, _wmiService, _processRunner);
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

        IsElevated = IsRunningAsAdministrator();
        ElevateCommand = new WpfRelayCommand(_ => RelaunchElevated(), _ => !IsElevated);
        LanguageService.LanguageChanged += OnLanguageChanged;

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
                StatusText = LocExtension.Format("StatusUpdateAvailable", veloUpdate.TargetFullRelease.Version);
                try
                {
                    await UpdateService.DownloadAndApplyAsync(veloUpdate, Log);
                    SetStatus(LocExtension.Format("StatusUpdateReady", veloUpdate.TargetFullRelease.Version));
                }
                catch
                {
                    SetStatus(
                        LocExtension.Format("StatusUpdateDownloadFailed", veloUpdate.TargetFullRelease.Version),
                        StatusSeverity.Warning);
                }
                return;
            }

            var result = await UpdateService.CheckForUpdateAsync();
            if (result is { available: true } update)
            {
                Log.Log($"Update available: v{update.version} - {update.url} ({update.verificationStatus}: {update.verificationDetail})");
                StatusText = LocExtension.Format("StatusUpdateAvailableVerified", update.version, update.verificationStatus);
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
        StatusText = LocExtension.Format("StatusThemeApplied", LocExtension.Get(ThemeService.Preference switch
        {
            ThemePreference.Light => "ThemeNameLight",
            ThemePreference.System => "ThemeNameSystem",
            _ => "ThemeNameDark"
        }));
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
            _dialog.ShowInfo(
                LocExtension.Format("LogExportedBody", path),
                LocExtension.Get("ExportCompleteTitle"));
        }
        catch (Exception ex)
        {
            Log.Log($"Log export failed: {ex.Message}");
            _dialog.ShowError(
                LocExtension.Format("LogExportFailed", ex.Message),
                LocExtension.Get("ExportErrorTitle"));
        }
    }

    public void OnClosing()
    {
        LanguageService.LanguageChanged -= OnLanguageChanged;
        DiskHealth.Dispose();
        SmartQueryService.Shutdown();
        Log.AutoSave();
    }

    private async Task RefreshCurrentAsync()
    {
        try
        {
            SetStatus("Refreshing current workspace...");
            await RefreshTabAsync(SelectedTabIndex);
            SetStatus(LocExtension.Get("Ready"));
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", StatusSeverity.Error);
            Log.Log($"Refresh error: {ex.Message}");
        }
    }

    private async Task OnTabChangedAsync(object? parameter)
    {
        try
        {
            var index = parameter is int i ? i : _selectedTabIndex;
            await RefreshTabAsync(index);
            SetStatus(LocExtension.Get("Ready"));
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", StatusSeverity.Error);
            Log.Log($"Tab switch error: {ex.Message}");
        }
    }

    private async Task RefreshTabAsync(int index)
    {
        switch (index)
        {
            case 0:
                StatusText = LocExtension.Get("StatusLoadingPartitions");
                await Partitions.LoadDisksAsync();
                break;
            case 1:
                StatusText = LocExtension.Get("StatusLoadingSnapshots");
                await Snapshots.RefreshAsync();
                break;
            case 2:
                StatusText = LocExtension.Get("StatusLoadingHealth");
                await DiskHealth.RefreshAsync();
                break;
            case 3:
                StatusText = LocExtension.Get("StatusLoadingTools");
                await Tools.RefreshDriveListsAsync();
                break;
            case 4:
                StatusText = LocExtension.Get("StatusLoadingImages");
                await DiskImages.RefreshAsync();
                break;
            case 5:
                StatusText = LocExtension.Get("StatusLoadingDrives");
                await DiskUsage.RefreshDrivesAsync();
                break;
            case 6:
                StatusText = LocExtension.Get("StatusLoadingCloning");
                await DiskCloning.RefreshAsync();
                break;
            case 7:
                StatusText = LocExtension.Get("StatusLoadingHex");
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
            StatusText = LocExtension.Get("StatusGeneratingBundle");
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
            _dialog.ShowInfo(
                LocExtension.Format("SupportBundleExportedBody", dlg.FileName),
                LocExtension.Get("SupportBundleExportedTitle"));

            SetStatus(LocExtension.Get("Ready"));
        }
        catch (Exception ex)
        {
            Log.Log($"Support bundle export failed: {ex.Message}");
            _dialog.ShowError(
                LocExtension.Format("SupportBundleExportFailed", ex.Message),
                LocExtension.Get("ExportErrorTitle"));
            SetStatus(LocExtension.Get("Ready"));
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

    internal static string DetectElevationContextKey(bool isAdmin)
    {
        if (!isAdmin) return "ElevationStandard";

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var isAdminProtection = userProfile.Contains("ADMIN_", StringComparison.OrdinalIgnoreCase);
        return isAdminProtection ? "ElevationAdminProtection" : "ElevationLegacyUac";
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ThemeLabel = ThemeService.GetLabel();
        foreach (var label in LocalizedLabels)
            OnPropertyChanged(label);
    }

    public static string GetVersionText() => $"PartitionPilot v{UpdateService.GetCurrentVersion()}";
}
