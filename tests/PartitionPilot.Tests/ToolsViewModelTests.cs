namespace PartitionPilot.Tests;

public class ToolsViewModelTests
{
    [Fact]
    public void WipeModeFlags_AreMutuallyExclusive()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.WipeIsFullDisk);
        Assert.False(viewModel.WipeIsFreeSpace);
        Assert.False(viewModel.WipeIsNvmeSanitize);

        viewModel.WipeIsFreeSpace = true;

        Assert.True(viewModel.WipeIsFreeSpace);
        Assert.False(viewModel.WipeIsFullDisk);
        Assert.False(viewModel.WipeIsNvmeSanitize);

        viewModel.WipeIsNvmeSanitize = true;

        Assert.False(viewModel.WipeIsFreeSpace);
        Assert.False(viewModel.WipeIsFullDisk);
        Assert.True(viewModel.WipeIsNvmeSanitize);

        viewModel.WipeIsFullDisk = true;

        Assert.False(viewModel.WipeIsFreeSpace);
        Assert.True(viewModel.WipeIsFullDisk);
        Assert.False(viewModel.WipeIsNvmeSanitize);
    }

    [Fact]
    public void WipeMode_NotifiesAllModeFlags()
    {
        var viewModel = CreateViewModel();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.WipeIsNvmeSanitize = true;

        Assert.Contains(nameof(ToolsViewModel.WipeIsFreeSpace), changed);
        Assert.Contains(nameof(ToolsViewModel.WipeIsFullDisk), changed);
        Assert.Contains(nameof(ToolsViewModel.WipeIsNvmeSanitize), changed);
    }

    [Fact]
    public void PickDriveSelection_PreservesValidCurrentLetter()
    {
        var selected = ToolsViewModel.PickDriveSelection(['C', 'D', 'E'], 'd', autoSelect: true);

        Assert.Equal('D', selected);
    }

    [Fact]
    public void PickDriveSelection_ClearsStaleSelectionWhenAutoSelectIsDisabled()
    {
        var selected = ToolsViewModel.PickDriveSelection(['C', 'D'], 'E', autoSelect: false);

        Assert.Equal(default, selected);
    }

    [Fact]
    public void PickDriveSelection_AutoSelectsFirstAvailableLetter()
    {
        var selected = ToolsViewModel.PickDriveSelection(['E', 'C', 'D'], default, autoSelect: true);

        Assert.Equal('C', selected);
    }

    [Fact]
    public void PickDiskSelection_PreservesByDiskNumber()
    {
        var current = new DiskInfo { Number = 7, FriendlyName = "old" };
        var replacement = new DiskInfo { Number = 7, FriendlyName = "new" };

        var selected = ToolsViewModel.PickDiskSelection(
            [new DiskInfo { Number = 1 }, replacement],
            current,
            autoSelect: true);

        Assert.Same(replacement, selected);
    }

    [Fact]
    public void PickDiskSelection_ClearsMissingDiskWhenAutoSelectIsDisabled()
    {
        var selected = ToolsViewModel.PickDiskSelection(
            [new DiskInfo { Number = 1 }],
            new DiskInfo { Number = 9 },
            autoSelect: false);

        Assert.Null(selected);
    }

    [Fact]
    public void PickVolumeSelection_PreservesByDriveLetter()
    {
        var current = new VolumeInfo { DriveLetter = 'd', FileSystemLabel = "old" };
        var replacement = new VolumeInfo { DriveLetter = 'D', FileSystemLabel = "new" };

        var selected = ToolsViewModel.PickVolumeSelection(
            [new VolumeInfo { DriveLetter = 'C' }, replacement],
            current,
            autoSelect: true);

        Assert.Same(replacement, selected);
    }

    [Fact]
    public void PickVolumeSelection_AutoSelectsFirstLetteredVolume()
    {
        var first = new VolumeInfo { DriveLetter = 'C' };
        var selected = ToolsViewModel.PickVolumeSelection(
            [new VolumeInfo(), new VolumeInfo { DriveLetter = 'E' }, first],
            current: null,
            autoSelect: true);

        Assert.Same(first, selected);
    }

    private static ToolsViewModel CreateViewModel()
    {
        var runner = new ProcessRunner();
        var log = new ActivityLog();
        return new ToolsViewModel(new WmiDiskService(runner, log), runner, log, new NullDialogService());
    }

    private sealed class NullDialogService : IDialogService
    {
        public void ShowInfo(string message, string title) { }
        public void ShowWarning(string message, string title) { }
        public void ShowError(string message, string title) { }
        public bool Confirm(string message, string title) => true;
        public bool ConfirmWarning(string message, string title) => true;
        public bool ConfirmDanger(string message, string title) => true;
    }
}
