namespace PartitionPilot.Tests;

public class PartitionsViewModelTests
{
    [Fact]
    public async Task LoadDisksAsync_KeepsBusyUntilTriggeredPartitionLoadFinishes()
    {
        var wmi = new DelayedPartitionWmiService();
        var viewModel = new PartitionsViewModel(
            wmi,
            new ProcessRunner(),
            new TestLog(),
            new NullDialogService(),
            action => action());

        var diskLoad = viewModel.LoadDisksAsync();
        await wmi.PartitionRequestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await diskLoad;

        try
        {
            Assert.True(viewModel.IsBusy);
        }
        finally
        {
            var becameIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PartitionsViewModel.IsBusy) && !viewModel.IsBusy)
                    becameIdle.TrySetResult();
            };

            wmi.Partitions.TrySetResult([]);
            await becameIdle.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        }

        Assert.False(viewModel.IsBusy);
    }

    [Theory]
    [InlineData("Recovery", true)]
    [InlineData("recovery", true)]
    [InlineData("Basic", false)]
    [InlineData("System", false)]
    public void IsRecoveryPartition_UsesPartitionType(string type, bool expected)
    {
        var partition = new PartitionInfo { Type = type };

        Assert.Equal(expected, PartitionsViewModel.IsRecoveryPartition(partition));
    }

    [Fact]
    public void IsForwardAdjacentMergePair_AllowsNextPartitionOnSameDisk()
    {
        var primary = Partition(1, 'D', 0, 100);
        var secondary = Partition(2, 'E', 100, 50);
        var partitions = new[] { primary, secondary };

        Assert.True(PartitionsViewModel.IsForwardAdjacentMergePair(partitions, primary, secondary));
    }

    [Fact]
    public void IsForwardAdjacentMergePair_RejectsSkippedPartition()
    {
        var primary = Partition(1, 'D', 0, 100);
        var middle = Partition(2, 'E', 100, 50);
        var secondary = Partition(3, 'F', 150, 50);
        var partitions = new[] { primary, middle, secondary };

        Assert.False(PartitionsViewModel.IsForwardAdjacentMergePair(partitions, primary, secondary));
    }

    [Fact]
    public void IsForwardAdjacentMergePair_RejectsReverseMerge()
    {
        var primary = Partition(2, 'E', 100, 50);
        var secondary = Partition(1, 'D', 0, 100);
        var partitions = new[] { secondary, primary };

        Assert.False(PartitionsViewModel.IsForwardAdjacentMergePair(partitions, primary, secondary));
    }

    [Fact]
    public void IsForwardAdjacentMergePair_RejectsMissingDriveLetter()
    {
        var primary = Partition(1, 'D', 0, 100);
        var secondary = Partition(2, null, 100, 50);
        var partitions = new[] { primary, secondary };

        Assert.False(PartitionsViewModel.IsForwardAdjacentMergePair(partitions, primary, secondary));
    }

    private static PartitionInfo Partition(int number, char? letter, long offset, long size) => new()
    {
        DiskNumber = 0,
        PartitionNumber = number,
        DriveLetter = letter,
        Offset = offset,
        Size = size,
        Type = "Basic",
    };

    private sealed class DelayedPartitionWmiService : IWmiDiskService
    {
        public TaskCompletionSource PartitionRequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<List<PartitionInfo>> Partitions { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<List<DiskInfo>> GetDisksAsync() => Task.FromResult(new List<DiskInfo>
        {
            new() { Number = 0, FriendlyName = "Test disk", Size = 1_000_000, PartitionStyle = "GPT" }
        });

        public Task<List<PartitionInfo>> GetPartitionsAsync(int diskNumber)
        {
            PartitionRequestStarted.TrySetResult();
            return Partitions.Task;
        }

        public Task<List<VolumeInfo>> GetVolumesAsync() => Task.FromResult(new List<VolumeInfo>());
        public Task<List<PhysicalDiskInfo>> GetPhysicalDisksAsync() => Task.FromResult(new List<PhysicalDiskInfo>());
        public Task<SmartData?> GetSmartDataAsync(string deviceId) => Task.FromResult<SmartData?>(null);
        public Task<List<AlignmentInfo>> GetAlignmentAuditAsync() => Task.FromResult(new List<AlignmentInfo>());
        public Task<HashSet<char>> GetPagefileLocationsAsync() => Task.FromResult(new HashSet<char>());
        public Task<List<char>> GetAvailableLettersAsync() => Task.FromResult(new List<char>());
        public Task<(long Min, long Max)> GetPartitionSupportedSizeAsync(char driveLetter) => Task.FromResult((0L, 0L));
        public Task<List<MountedImageInfo>> GetMountedImagesAsync() => Task.FromResult(new List<MountedImageInfo>());
        public Task<Dictionary<char, string>> GetBitLockerStatusAsync() => Task.FromResult(new Dictionary<char, string>());
        public Task<List<string>> GetBitLockerProtectedTargetsAsync(int diskNumber) => Task.FromResult(new List<string>());
        public Task<Dictionary<int, string>> GetStoragePoolMembershipAsync() => Task.FromResult(new Dictionary<int, string>());
        public Task<Dictionary<string, (string Health, string Status, bool ReadOnly)>> GetStoragePoolHealthAsync() =>
            Task.FromResult(new Dictionary<string, (string Health, string Status, bool ReadOnly)>());
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

    private sealed class TestLog : IActivityLog
    {
        public void Log(string message) { }
    }
}
