using System.Windows.Input;

namespace PartitionPilot;

public class MainViewModel : ViewModelBase
{
    private readonly ProcessRunner _processRunner;
    private readonly WmiDiskService _wmiService;
    private readonly IDialogService _dialog;

    public ActivityLog Log { get; }
    public PartitionsViewModel Partitions { get; }
    public DiskHealthViewModel DiskHealth { get; }
    public ToolsViewModel Tools { get; }
    public DiskImagesViewModel DiskImages { get; }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
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

        TabChangedCommand = new AsyncRelayCommand(OnTabChangedAsync);
        ExportLogCommand = new RelayCommand(_ => ExportLog());
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());

        Log.Log("PartitionPilot ready.");
    }

    private void ToggleTheme()
    {
        ThemeService.Toggle();
        ThemeLabel = ThemeService.IsDarkMode ? "Light Mode" : "Dark Mode";
        Log.Log($"Theme switched to {(ThemeService.IsDarkMode ? "dark" : "light")} mode.");
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

    private async Task OnTabChangedAsync(object? parameter)
    {
        try
        {
            var index = parameter is int i ? i : _selectedTabIndex;

            switch (index)
            {
                case 0:
                    // Partitions tab is loaded on demand via its own RefreshCommand
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
            }

            StatusText = "Ready";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            Log.Log($"Tab switch error: {ex.Message}");
        }
    }
}
