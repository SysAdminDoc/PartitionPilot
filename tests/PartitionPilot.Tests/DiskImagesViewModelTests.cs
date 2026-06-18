namespace PartitionPilot.Tests;

public class DiskImagesViewModelTests
{
    [Fact]
    public void VhdIsFixed_MirrorsDynamicDiskType()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.VhdIsDynamic);
        Assert.False(viewModel.VhdIsFixed);

        viewModel.VhdIsFixed = true;

        Assert.False(viewModel.VhdIsDynamic);
        Assert.True(viewModel.VhdIsFixed);

        viewModel.VhdIsDynamic = true;

        Assert.True(viewModel.VhdIsDynamic);
        Assert.False(viewModel.VhdIsFixed);
    }

    private static DiskImagesViewModel CreateViewModel()
    {
        var runner = new ProcessRunner();
        var log = new ActivityLog();
        return new DiskImagesViewModel(runner, new WmiDiskService(runner, log), log, new NullDialogService());
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
