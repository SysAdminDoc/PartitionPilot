using System.Security.Principal;
using System.Windows.Input;

namespace PartitionPilot;

public class MainViewModel : ViewModelBase
{
    public const string AppVersionText = "PartitionPilot v0.2.3";

    private readonly ProcessRunner _processRunner;
    private readonly WmiDiskService _wmiService;
    private readonly IDialogService _dialog;

    public ActivityLog Log { get; }
    public PartitionsViewModel Partitions { get; }
    public DiskHealthViewModel DiskHealth { get; }
    public ToolsViewModel Tools { get; }
    public DiskImagesViewModel DiskImages { get; }
    public DiskUsageViewModel DiskUsage { get; }
    public DiskCloningViewModel DiskCloning { get; }

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
    public ICommand ToggleThemeCommand { get; }
    public ICommand RefreshCurrentCommand { get; }

    public string VersionText => AppVersionText;
    public string AdminSessionText { get; }
    public string AdminSessionDetail { get; }
    public string SessionStateText => "Session state";
    public string SessionStateDetail => StatusText;

    private string _themeLabel = ThemeService.IsDarkMode ? "Light Mode" : "Dark Mode";
    public string ThemeLabel
    {
        get => _themeLabel;
        set => SetProperty(ref _themeLabel, value);
    }

    public MainViewModel()
    {
        _processRunner = new ProcessRunner();
        Log = new ActivityLog();
        _wmiService = new WmiDiskService(_processRunner, Log);
        _dialog = new MessageBoxDialogService();

        Partitions = new PartitionsViewModel(_wmiService, _processRunner, Log, _dialog);
        DiskHealth = new DiskHealthViewModel(_wmiService, Log);
        Tools = new ToolsViewModel(_wmiService, _processRunner, Log, _dialog);
        DiskImages = new DiskImagesViewModel(_processRunner, _wmiService, Log, _dialog);
        DiskUsage = new DiskUsageViewModel(_wmiService, Log);
        DiskCloning = new DiskCloningViewModel(_processRunner, _wmiService, Log, _dialog);

        TabChangedCommand = new AsyncRelayCommand(OnTabChangedAsync);
        ExportLogCommand = new RelayCommand(_ => ExportLog());
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
        RefreshCurrentCommand = new AsyncRelayCommand(_ => RefreshCurrentAsync());

        var isAdmin = IsRunningAsAdministrator();
        AdminSessionText = isAdmin ? "Admin session" : "Standard session";
        AdminSessionDetail = isAdmin ? "Disk changes available" : "Run as administrator for write operations";

        Log.Log("PartitionPilot ready.");
        _ = CheckForUpdateAsync();
        _ = Partitions.LoadDisksAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        var result = await UpdateService.CheckForUpdateAsync();
        if (result is { available: true } update)
        {
            Log.Log($"Update available: v{update.version} - {update.url}");
            StatusText = $"Update available: v{update.version}";
        }
    }

    private void ToggleTheme()
    {
        ThemeService.Toggle();
        ThemeLabel = ThemeService.IsDarkMode ? "Light Mode" : "Dark Mode";
        Log.Log($"Theme applied: {(ThemeService.IsDarkMode ? "dark" : "light")} mode.");
        StatusText = $"{(ThemeService.IsDarkMode ? "Dark" : "Light")} theme applied";
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
                StatusText = "Loading disk health data...";
                await DiskHealth.RefreshAsync();
                break;
            case 2:
                StatusText = "Loading tools drive lists...";
                await Tools.RefreshDriveListsAsync();
                break;
            case 3:
                StatusText = "Loading disk images...";
                await DiskImages.RefreshAsync();
                break;
            case 4:
                StatusText = "Loading drive list...";
                await DiskUsage.RefreshDrivesAsync();
                break;
            case 5:
                StatusText = "Loading cloning data...";
                await DiskCloning.RefreshAsync();
                break;
        }
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
