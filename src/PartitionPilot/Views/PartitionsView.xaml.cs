using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PartitionPilot.Dialogs;

namespace PartitionPilot.Views;

public partial class PartitionsView : UserControl
{
    public PartitionsView() => InitializeComponent();

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
        if (freeGB < 0.01) { MessageBox.Show("No unallocated space.", "Create", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var dlg = new CreatePartitionDialog(freeGB, available) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            await vm.ExecuteCreateAsync(dlg.SizeGB, dlg.SelectedLetter, dlg.FileSystem, dlg.VolumeLabel, dlg.QuickFormat);
    }

    private async void OnFormat(object sender, RoutedEventArgs e)
    {
        if (VM is not { SelectedPartition: { DriveLetter: not null } part } vm) { MessageBox.Show("Select a partition with a drive letter.", "Format", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (part.IsCritical)
        {
            var warn = MessageBox.Show(
                $"CRITICAL: {part.DriveLetter}: is a {part.Type} partition" +
                (part.IsBoot ? " (Boot)" : "") + (part.IsSystem ? " (System)" : "") +
                ".\n\nFormatting may make the system unbootable.\n\nAre you absolutely sure?",
                "Format Critical Partition", MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.No);
            if (warn != MessageBoxResult.Yes) return;
        }
        var dlg = new FormatPartitionDialog(part.DriveLetter.Value, part.FileSystem, part.Size) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        var confirm = MessageBox.Show(
            $"Format {part.DriveLetter}: as {dlg.FileSystem}?\n\nALL DATA ON THIS VOLUME WILL BE ERASED.\n\nThis action cannot be undone.",
            "Confirm Format", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirm == MessageBoxResult.Yes)
            await vm.ExecuteFormatAsync(part.DriveLetter.Value, dlg.FileSystem, dlg.VolumeLabel, dlg.QuickFormat, dlg.AllocationUnitSize);
    }

    private async void OnResize(object sender, RoutedEventArgs e)
    {
        if (VM is not { SelectedPartition: { DriveLetter: not null } part } vm) { MessageBox.Show("Select a partition with a drive letter.", "Resize", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var (min, max) = await vm.GetSupportedSizeAsync(part.DriveLetter.Value);
        var dlg = new ResizePartitionDialog(part.DriveLetter.Value, part.Size, min, max) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            await vm.ExecuteResizeAsync(part.DriveLetter.Value, dlg.NewSizeBytes);
    }

    private async void OnSplit(object sender, RoutedEventArgs e)
    {
        if (VM is not { SelectedPartition: { DriveLetter: not null } part } vm) { MessageBox.Show("Select a partition with a drive letter.", "Split", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var (min, _) = await vm.GetSupportedSizeAsync(part.DriveLetter.Value);
        var available = await vm.GetAvailableLettersAsync();
        var maxNewGB = Math.Floor((part.Size - min) / (double)(1L << 30));
        if (maxNewGB < 1) { MessageBox.Show("Not enough space to split.", "Split", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var dlg = new SplitPartitionDialog(part.DriveLetter.Value, part.Size, min, maxNewGB, available) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            await vm.ExecuteSplitAsync(part.DriveLetter.Value, dlg.NewPartSizeGB, dlg.NewLetter, dlg.FileSystem, dlg.VolumeLabel);
    }

    private async void OnChangeLetter(object sender, RoutedEventArgs e)
    {
        if (VM is not { SelectedPartition: { } part } vm) { MessageBox.Show("Select a partition.", "Letter", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var available = await vm.GetAvailableLettersAsync();
        var dlg = new ChangeLetterDialog(part.DriveLetter, available) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            await vm.ExecuteChangeLetterAsync(part.PartitionNumber, dlg.NewLetter);
    }
}
