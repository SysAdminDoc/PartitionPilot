using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace PartitionPilot;

public class DiskImagesViewModel : ViewModelBase
{
    private readonly ProcessRunner _processRunner;
    private readonly WmiDiskService _wmiService;
    private readonly ActivityLog _log;
    private readonly IDialogService _dialog;

    // ──────────────────────── Mount ────────────────────────

    private string _mountPath = "";
    public string MountPath
    {
        get => _mountPath;
        set
        {
            if (SetProperty(ref _mountPath, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public ObservableCollection<MountedImageInfo> MountedImages { get; } = new();

    private MountedImageInfo? _selectedMountedImage;
    public MountedImageInfo? SelectedMountedImage
    {
        get => _selectedMountedImage;
        set
        {
            if (SetProperty(ref _selectedMountedImage, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    // ──────────────────────── Create VHD ────────────────────────

    private string _vhdPath = "";
    public string VhdPath
    {
        get => _vhdPath;
        set
        {
            if (SetProperty(ref _vhdPath, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private double _vhdSizeGB = 10;
    public double VhdSizeGB
    {
        get => _vhdSizeGB;
        set
        {
            if (SetProperty(ref _vhdSizeGB, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private bool _vhdIsDynamic = true;
    public bool VhdIsDynamic
    {
        get => _vhdIsDynamic;
        set => SetProperty(ref _vhdIsDynamic, value);
    }

    private string _vhdFileSystem = "NTFS";
    public string VhdFileSystem
    {
        get => _vhdFileSystem;
        set => SetProperty(ref _vhdFileSystem, value);
    }

    // ──────────────────────── Shared ────────────────────────

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    // ──────────────────────── Commands ────────────────────────

    public ICommand BrowseMountCommand { get; }
    public ICommand MountCommand { get; }
    public ICommand DismountCommand { get; }
    public ICommand BrowseVhdCommand { get; }
    public ICommand CreateVhdCommand { get; }
    public ICommand RefreshCommand { get; }

    public DiskImagesViewModel(ProcessRunner processRunner, WmiDiskService wmiService, ActivityLog log, IDialogService dialog)
    {
        _processRunner = processRunner;
        _wmiService = wmiService;
        _log = log;
        _dialog = dialog;

        BrowseMountCommand = new RelayCommand(_ => BrowseMountPath());
        MountCommand = new AsyncRelayCommand(_ => MountAsync(), _ => !string.IsNullOrWhiteSpace(MountPath));
        DismountCommand = new AsyncRelayCommand(_ => DismountAsync(), _ => SelectedMountedImage is not null);
        BrowseVhdCommand = new RelayCommand(_ => BrowseVhdPath());
        CreateVhdCommand = new AsyncRelayCommand(_ => CreateVhdAsync(), _ => !string.IsNullOrWhiteSpace(VhdPath) && VhdSizeGB > 0);
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
    }

    // ──────────────────────── Browse ────────────────────────

    private void BrowseMountPath()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Disk Image",
            Filter = "Disk Images (*.iso;*.vhd;*.vhdx)|*.iso;*.vhd;*.vhdx|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dlg.ShowDialog() == true)
            MountPath = dlg.FileName;
    }

    private void BrowseVhdPath()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Create VHD File",
            Filter = "VHDX Files (*.vhdx)|*.vhdx|VHD Files (*.vhd)|*.vhd",
            DefaultExt = ".vhdx",
            OverwritePrompt = true
        };

        if (dlg.ShowDialog() == true)
            VhdPath = dlg.FileName;
    }

    // ──────────────────────── Mount / Dismount ────────────────────────

    private async Task MountAsync()
    {
        if (string.IsNullOrWhiteSpace(MountPath)) return;

        IsBusy = true;
        try
        {
            _log.Log($"Mounting disk image: {MountPath}...");

            var cmd = $"Mount-DiskImage -ImagePath '{MountPath.Replace("'", "''")}' -PassThru | Get-Volume";
            var result = await _processRunner.RunPowerShellAsync(cmd, _log);
            _log.Log($"Mount result: {result.Trim()}");

            await RefreshAsync();

            _dialog.ShowInfo($"Image mounted successfully.\n\n{result.Trim()}", "Mount Complete");
        }
        catch (Exception ex)
        {
            _log.Log($"Mount failed: {ex.Message}");
            _dialog.ShowError($"Failed to mount image:\n{ex.Message}", "Mount Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DismountAsync()
    {
        if (SelectedMountedImage is null) return;

        IsBusy = true;
        try
        {
            string imagePath = SelectedMountedImage.ImagePath;
            _log.Log($"Dismounting disk image: {imagePath}...");

            var cmd = $"Dismount-DiskImage -ImagePath '{imagePath.Replace("'", "''")}'";
            var result = await _processRunner.RunPowerShellAsync(cmd, _log);
            _log.Log($"Dismount result: {result.Trim()}");

            SelectedMountedImage = null;
            await RefreshAsync();

            _dialog.ShowInfo("Image dismounted successfully.", "Dismount Complete");
        }
        catch (Exception ex)
        {
            _log.Log($"Dismount failed: {ex.Message}");
            _dialog.ShowError($"Failed to dismount image:\n{ex.Message}", "Dismount Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ──────────────────────── Create VHD ────────────────────────

    private async Task CreateVhdAsync()
    {
        if (string.IsNullOrWhiteSpace(VhdPath) || VhdSizeGB <= 0) return;

        IsBusy = true;
        try
        {
            long sizeMB = (long)(VhdSizeGB * 1024);
            string diskType = VhdIsDynamic ? "expandable" : "fixed";
            string ext = System.IO.Path.GetExtension(VhdPath).ToLowerInvariant();
            string vdiskType = ext == ".vhdx" ? "vhdx" : "vhd";

            _log.Log($"Creating {vdiskType.ToUpper()} ({diskType}): {VhdPath}, {VhdSizeGB:F1} GB...");

            string script = $"""
                create vdisk file="{VhdPath}" maximum={sizeMB} type={diskType}
                select vdisk file="{VhdPath}"
                attach vdisk
                create partition primary
                format fs={VhdFileSystem} label="NewVHD" quick
                assign
                """;

            var result = await _processRunner.RunDiskpartAsync(script, _log);
            _log.Log($"Create VHD result: {result.Trim()}");

            await RefreshAsync();

            _dialog.ShowInfo($"VHD created and mounted successfully.\n\nPath: {VhdPath}\nSize: {VhdSizeGB:F1} GB",
                "VHD Created");
        }
        catch (Exception ex)
        {
            _log.Log($"Create VHD failed: {ex.Message}");
            _dialog.ShowError($"Failed to create VHD:\n{ex.Message}", "Create VHD Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ──────────────────────── Refresh ────────────────────────

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            _log.Log("Refreshing mounted disk images...");
            var images = await _wmiService.GetMountedImagesAsync();

            Application.Current.Dispatcher.Invoke(() =>
            {
                MountedImages.Clear();
                foreach (var img in images)
                    MountedImages.Add(img);
            });

            _log.Log($"Found {images.Count} mounted image(s).");
        }
        catch (Exception ex)
        {
            _log.Log($"Error refreshing mounted images: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
