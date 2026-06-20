using System.IO;
using System.IO.Compression;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        TabChangedCommand = new AsyncRelayCommand(OnTabChangedAsync);
        ExportLogCommand = new WpfRelayCommand(_ => ExportLog());
        ExportSupportBundleCommand = new AsyncRelayCommand(_ => ExportSupportBundleAsync());
        ToggleThemeCommand = new WpfRelayCommand(_ => ToggleTheme());
        RefreshCurrentCommand = new AsyncRelayCommand(_ => RefreshCurrentAsync());

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
            Log.Log($"Update available: v{update.version} - {update.url}");
            StatusText = $"Update available: v{update.version}";
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

            var tempDir = Path.Combine(Path.GetTempPath(), $"pp_support_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var info = new
                {
                    AppVersion = GetVersionText(),
                    OSVersion = Environment.OSVersion.VersionString,
                    OSBuild = Environment.OSVersion.Version.Build,
                    Is64Bit = Environment.Is64BitOperatingSystem,
                    IsAdmin = IsRunningAsAdministrator(),
                    ElevationContext = ElevationContextText,
                    DotNetVersion = Environment.Version.ToString(),
                    Timestamp = DateTimeOffset.Now.ToString("o")
                };
                await File.WriteAllTextAsync(
                    Path.Combine(tempDir, "system-info.json"),
                    JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));

                await File.WriteAllTextAsync(
                    Path.Combine(tempDir, "activity-log.txt"),
                    RedactSupportBundleText(Log.FullText));

                var disks = await _wmiService.GetDisksAsync();
                var redactedDisks = disks.Select(d => new
                {
                    d.Number,
                    d.FriendlyName,
                    Size = SizeUtil.Format(d.Size),
                    d.PartitionStyle,
                    d.NumberOfPartitions,
                    d.StoragePoolName
                });
                await File.WriteAllTextAsync(
                    Path.Combine(tempDir, "disk-summary.json"),
                    JsonSerializer.Serialize(redactedDisks, new JsonSerializerOptions { WriteIndented = true }));

                var snapshotDir = PartitionTableBackup.BackupDirectory;
                if (Directory.Exists(snapshotDir))
                {
                    var snapshotOut = Path.Combine(tempDir, "snapshots");
                    Directory.CreateDirectory(snapshotOut);
                    foreach (var file in Directory.EnumerateFiles(snapshotDir, "*.json").Take(10))
                    {
                        var content = await File.ReadAllTextAsync(file);
                        content = RedactSupportBundleText(content);
                        await File.WriteAllTextAsync(
                            Path.Combine(snapshotOut, Path.GetFileName(file)),
                            content);
                    }
                }

                if (File.Exists(dlg.FileName))
                    File.Delete(dlg.FileName);
                ZipFile.CreateFromDirectory(tempDir, dlg.FileName);

                Log.Log($"Support bundle exported to: {dlg.FileName}");
                _dialog.ShowInfo($"Support bundle exported to:\n{dlg.FileName}\n\nSerial numbers and user paths have been redacted.",
                    "Support Bundle Exported");
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }

            StatusText = "Ready";
        }
        catch (Exception ex)
        {
            Log.Log($"Support bundle export failed: {ex.Message}");
            _dialog.ShowError($"Failed to export support bundle:\n{ex.Message}", "Export Error");
            StatusText = "Ready";
        }
    }

    [GeneratedRegex("""(?i)("serial(?:number)?"\s*:\s*")[^"]*(")""")]
    private static partial Regex JsonSerialPattern();

    [GeneratedRegex("""(?i)(serial(?:number)?\s*[:=]\s*)[^\s,;]+""")]
    private static partial Regex TextSerialPattern();

    [GeneratedRegex("""(?i)[A-Z]:\\[^\r\n'"]+""")]
    private static partial Regex SupportPathPattern();

    public static string RedactSupportBundleText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var redacted = SupportPathPattern().Replace(text, "[path]");
        redacted = JsonSerialPattern().Replace(redacted, "$1[redacted]$2");
        redacted = TextSerialPattern().Replace(redacted, "$1[redacted]");
        return redacted;
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
