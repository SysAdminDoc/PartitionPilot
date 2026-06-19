using System.Collections.ObjectModel;
using System.Windows;

namespace PartitionPilot.Dialogs;

public partial class MergePartitionDialog : Window
{
    private readonly List<PartitionInfo> _mergeable;

    public PartitionInfo? PrimaryPartition => PrimaryCombo.SelectedItem as PartitionInfo;
    public PartitionInfo? SecondaryPartition => SecondaryCombo.SelectedItem as PartitionInfo;

    public MergePartitionDialog(ObservableCollection<PartitionInfo> partitions)
    {
        InitializeComponent();

        _mergeable = partitions
            .Where(p => p.DriveLetter.HasValue && !p.IsCritical && !p.IsUnsupportedType)
            .OrderBy(p => p.Offset)
            .ThenBy(p => p.PartitionNumber)
            .ToList();

        PrimaryCombo.ItemsSource = _mergeable;

        if (_mergeable.Count >= 2)
            PrimaryCombo.SelectedIndex = 0;

        RefreshSecondaryChoices();
    }

    private void OnPrimaryChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RefreshSecondaryChoices();
    }

    private void RefreshSecondaryChoices()
    {
        var primary = PrimaryPartition;
        var candidates = primary is null
            ? new List<PartitionInfo>()
            : _mergeable
                .Where(p => PartitionsViewModel.IsForwardAdjacentMergePair(_mergeable, primary, p))
                .ToList();

        SecondaryCombo.ItemsSource = candidates;
        SecondaryCombo.SelectedIndex = candidates.Count > 0 ? 0 : -1;
        MergeButton.IsEnabled = candidates.Count > 0;

        MergeHelpText.Text = candidates.Count > 0
            ? "The partition to remove will be deleted. The primary partition will be extended into the freed space when the queued operation is applied."
            : "Choose a primary data partition that is immediately followed by another mergeable data partition.";
    }

    private void OnMerge(object sender, RoutedEventArgs e)
    {
        if (PrimaryPartition is null || SecondaryPartition is null)
        {
            MessageBox.Show(
                "Choose a primary partition that is immediately followed by another mergeable partition.",
                "Merge Partitions",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (PrimaryPartition == SecondaryPartition)
        {
            MessageBox.Show("Select two adjacent partitions.", "Merge Partitions", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }
}
