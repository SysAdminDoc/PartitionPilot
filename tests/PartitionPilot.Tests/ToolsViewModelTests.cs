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
