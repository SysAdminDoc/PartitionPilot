using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PartitionPilot.Controls;

public partial class DiskBarControl : UserControl
{
    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.Register(nameof(Segments), typeof(ObservableCollection<DiskBarSegment>),
            typeof(DiskBarControl), new PropertyMetadata(null, OnSegmentsChanged));

    public ObservableCollection<DiskBarSegment>? Segments
    {
        get => (ObservableCollection<DiskBarSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public DiskBarControl() => InitializeComponent();

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DiskBarControl control)
        {
            if (e.OldValue is INotifyCollectionChanged old)
                old.CollectionChanged -= control.OnCollectionChanged;
            if (e.NewValue is INotifyCollectionChanged @new)
                @new.CollectionChanged += control.OnCollectionChanged;
            control.Rebuild();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        BarGrid.Children.Clear();
        BarGrid.ColumnDefinitions.Clear();

        var segments = Segments;
        if (segments is null || segments.Count == 0)
        {
            BarGrid.Children.Add(new TextBlock
            {
                Text = "No partition map loaded",
                Foreground = new SolidColorBrush(Color.FromRgb(116, 128, 140)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            return;
        }

        var converter = new BrushConverter();
        int col = 0;
        foreach (var seg in segments)
        {
            var colDef = new ColumnDefinition
            {
                Width = new GridLength(Math.Max(seg.Proportion, 0.018), GridUnitType.Star)
            };
            BarGrid.ColumnDefinitions.Add(colDef);

            var border = new Border
            {
                Background = converter.ConvertFromString(seg.ColorHex) as Brush ?? Brushes.Gray,
                Margin = new Thickness(1.5, 6, 1.5, 6),
                CornerRadius = new CornerRadius(4),
                ToolTip = $"{seg.Type}: {SizeUtil.Format(seg.SizeBytes)}"
            };

            var tb = new TextBlock
            {
                Text = seg.Label,
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(2, 0, 2, 0)
            };

            border.Child = tb;
            Grid.SetColumn(border, col);
            BarGrid.Children.Add(border);
            col++;
        }
    }
}
