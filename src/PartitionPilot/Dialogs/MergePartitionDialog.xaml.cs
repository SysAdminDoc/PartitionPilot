using System.Collections.ObjectModel;
using System.Windows;

namespace PartitionPilot.Dialogs;

public partial class MergePartitionDialog : Window
{
    public PartitionInfo? PrimaryPartition => PrimaryCombo.SelectedItem as PartitionInfo;
    public PartitionInfo? SecondaryPartition => SecondaryCombo.SelectedItem as PartitionInfo;

    public MergePartitionDialog(ObservableCollection<PartitionInfo> partitions)
    {
        InitializeComponent();

        var mergeable = partitions
            .Where(p => p.DriveLetter.HasValue && !p.IsCritical && !p.IsUnsupportedType)
            .OrderBy(p => p.PartitionNumber)
            .ToList();

        PrimaryCombo.ItemsSource = mergeable;
        SecondaryCombo.ItemsSource = mergeable;

        if (mergeable.Count >= 2)
        {
            PrimaryCombo.SelectedIndex = 0;
            SecondaryCombo.SelectedIndex = 1;
        }
    }

    private void OnMerge(object sender, RoutedEventArgs e)
    {
        if (PrimaryPartition is null || SecondaryPartition is null)
            return;

        if (PrimaryPartition == SecondaryPartition)
        {
            MessageBox.Show("Select two different partitions.", "Merge Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }
}
