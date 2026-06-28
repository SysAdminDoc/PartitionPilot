using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PartitionPilot.Dialogs;

namespace PartitionPilot.Views;

public partial class PartitionsView : UserControl
{
    private readonly IDialogService _dialog;

    public PartitionsView() : this(new MessageBoxDialogService())
    {
    }

    internal PartitionsView(IDialogService dialog)
    {
        _dialog = dialog;
        InitializeComponent();
    }

    private PartitionsViewModel? VM => DataContext as PartitionsViewModel;

    private void OnPartitionRowRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private async void OnCreate(object sender, RoutedEventArgs e)
    {
        if (VM is not { SelectedDisk: not null } vm) return;
        var available = await vm.GetAvailableLettersAsync();
        var freeGB = Math.Round(vm.SelectedDisk.LargestFreeExtent / (double)(1L << 30), 2);
        if (freeGB < 0.01) { _dialog.ShowWarning("No unallocated space is available on the selected disk.", "Create Partition"); return; }
        var dlg = new CreatePartitionDialog(freeGB, available) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            await vm.ExecuteCreateAsync(dlg.SizeGB, dlg.SelectedLetter, dlg.FileSystem, dlg.VolumeLabel, dlg.QuickFormat);
    }

    private async void OnFormat(object sender, RoutedEventArgs e)
    {
        if (VM is not { SelectedPartition: { DriveLetter: not null } part } vm) { _dialog.ShowInfo("Select a partition with a drive letter before formatting.", "Format Partition"); return; }
        var targetLine = vm.SelectedDisk is null ? "" : $"Target:\n{vm.SelectedDisk.ConfirmationSummary}\n\n";
        if (part.IsCritical)
        {
            var warn = _dialog.ConfirmDanger(
                $"CRITICAL: {part.DriveLetter}: is a {part.Type} partition" +
                (part.IsBoot ? " (Boot)" : "") + (part.IsSystem ? " (System)" : "") +
                $".\n\nFormatting may make the system unbootable.\n\n{targetLine}Are you absolutely sure?",
                "Format Critical Partition");
            if (!warn) return;
        }
        var dlg = new FormatPartitionDialog(part.DriveLetter.Value, part.FileSystem, part.Size) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        var encryptionLine = string.IsNullOrWhiteSpace(part.EncryptionStatus)
            ? ""
            : $"\nEncryption: {part.EncryptionStatus}\n";
        var confirm = _dialog.ConfirmWarning(
            $"Format {part.DriveLetter}: as {dlg.FileSystem}?\n\n{targetLine}{encryptionLine}\nALL DATA ON THIS VOLUME WILL BE ERASED.\n\nThis action cannot be undone.",
            "Confirm Format");
        if (confirm)
            await vm.ExecuteFormatAsync(part.DriveLetter.Value, dlg.FileSystem, dlg.VolumeLabel, dlg.QuickFormat, dlg.AllocationUnitSize);
    }

    private async void OnResize(object sender, RoutedEventArgs e)
    {
        if (VM is not { SelectedPartition: { DriveLetter: not null } part } vm) { _dialog.ShowInfo("Select a partition with a drive letter before resizing.", "Resize Partition"); return; }
        var (min, max) = await vm.GetSupportedSizeAsync(part.DriveLetter.Value);
        var dlg = new ResizePartitionDialog(part.DriveLetter.Value, part.Size, min, max) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            await vm.ExecuteResizeAsync(part.DriveLetter.Value, dlg.NewSizeBytes);
    }

    private async void OnSplit(object sender, RoutedEventArgs e)
    {
        if (VM is not { SelectedPartition: { DriveLetter: not null } part } vm) { _dialog.ShowInfo("Select a partition with a drive letter before splitting.", "Split Partition"); return; }
        var (min, _) = await vm.GetSupportedSizeAsync(part.DriveLetter.Value);
        var available = await vm.GetAvailableLettersAsync();
        var maxNewGB = Math.Floor((part.Size - min) / (double)(1L << 30));
        if (maxNewGB < 1) { _dialog.ShowWarning("Not enough free space remains after the minimum supported size to create a new partition.", "Split Partition"); return; }
        var dlg = new SplitPartitionDialog(part.DriveLetter.Value, part.Size, min, maxNewGB, available) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            await vm.ExecuteSplitAsync(part.DriveLetter.Value, dlg.NewPartSizeGB, dlg.NewLetter, dlg.FileSystem, dlg.VolumeLabel);
    }

    private async void OnMerge(object sender, RoutedEventArgs e)
    {
        if (VM is not { } vm || vm.Partitions.Count < 2) { _dialog.ShowInfo("At least two partitions are needed for a merge.", "Merge Partitions"); return; }
        var dlg = new MergePartitionDialog(vm.Partitions) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true && dlg.PrimaryPartition is not null && dlg.SecondaryPartition is not null)
            await vm.ExecuteMergeAsync(dlg.PrimaryPartition, dlg.SecondaryPartition);
    }

    private async void OnChangeLetter(object sender, RoutedEventArgs e)
    {
        if (VM is not { SelectedPartition: { } part } vm) { _dialog.ShowInfo("Select a partition before changing its drive letter.", "Change Drive Letter"); return; }
        var available = await vm.GetAvailableLettersAsync();
        var dlg = new ChangeLetterDialog(part.DriveLetter, available) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            await vm.ExecuteChangeLetterAsync(part.PartitionNumber, dlg.NewLetter);
    }
}
