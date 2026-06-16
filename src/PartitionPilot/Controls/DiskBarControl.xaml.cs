using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation;
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
            var mutedBrush = TryFindResource("MutedTextBrush") as Brush
                             ?? new SolidColorBrush(Color.FromRgb(116, 128, 140));
            var subtextBrush = TryFindResource("SubtextBrush") as Brush ?? mutedBrush;
            var empty = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            empty.Children.Add(new TextBlock
            {
                Text = "No partition map loaded",
                Foreground = mutedBrush,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            empty.Children.Add(new TextBlock
            {
                Text = "Refresh disks and select a disk to render its layout.",
                Foreground = subtextBrush,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            BarGrid.Children.Add(empty);
            return;
        }

        var converter = new BrushConverter();
        var segmentBorderBrush = TryFindResource("TableRuleBrush") as Brush
                                 ?? new SolidColorBrush(Color.FromRgb(41, 48, 57));
        int col = 0;
        foreach (var seg in segments)
        {
            var colDef = new ColumnDefinition
            {
                Width = new GridLength(Math.Max(seg.Proportion, 0.018), GridUnitType.Star)
            };
            BarGrid.ColumnDefinitions.Add(colDef);

            var fillBrush = converter.ConvertFromString(seg.ColorHex) as SolidColorBrush
                            ?? new SolidColorBrush(Colors.Gray);
            var border = new Border
            {
                Background = fillBrush,
                BorderBrush = segmentBorderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(1.5, 5, 1.5, 5),
                CornerRadius = new CornerRadius(4),
                ToolTip = $"{seg.Type}: {SizeUtil.Format(seg.SizeBytes)}"
            };
            AutomationProperties.SetName(border, $"{seg.Label}, {seg.Type}, {SizeUtil.Format(seg.SizeBytes)}");

            var tb = new TextBlock
            {
                Text = seg.Label,
                Foreground = GetReadableSegmentTextBrush(fillBrush.Color),
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

    private static Brush GetReadableSegmentTextBrush(Color background)
    {
        var luminance = (0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B);
        return luminance > 145
            ? new SolidColorBrush(Color.FromRgb(7, 17, 22))
            : Brushes.White;
    }
}
