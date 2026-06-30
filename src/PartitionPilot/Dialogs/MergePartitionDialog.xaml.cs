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
            ? LocExtension.Get("DialogMergeDeleteExtends")
            : LocExtension.Get("DialogMergeChoosePrimaryDataPartition");
    }

    private void OnMerge(object sender, RoutedEventArgs e)
    {
        if (PrimaryPartition is null || SecondaryPartition is null)
        {
            MessageBox.Show(
                LocExtension.Get("DialogMergePrimaryAdjacentRequired"),
                LocExtension.Get("Xaml_MergePartitions"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (PrimaryPartition == SecondaryPartition)
        {
            MessageBox.Show(
                LocExtension.Get("DialogMergeTwoAdjacentRequired"),
                LocExtension.Get("Xaml_MergePartitions"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }
}
