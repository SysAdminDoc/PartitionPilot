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
        var segmentSurfaceBrush = TryFindResource("CardBgBrush") as Brush
                                  ?? new SolidColorBrush(Color.FromRgb(32, 36, 42));
        var segmentTextBrush = TryFindResource("TextBrush") as Brush ?? Brushes.White;
        var segmentSubtextBrush = TryFindResource("SubtextBrush") as Brush
                                  ?? new SolidColorBrush(Color.FromRgb(170, 180, 192));
        var segmentMutedBrush = TryFindResource("MutedTextBrush") as Brush
                                ?? new SolidColorBrush(Color.FromRgb(116, 128, 140));
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
                Background = segmentSurfaceBrush,
                BorderBrush = segmentBorderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(2, 4, 2, 4),
                CornerRadius = new CornerRadius(4),
                ToolTip = $"{seg.Label} - {seg.Type}, {seg.SizeText}",
                ClipToBounds = true,
                Focusable = true
            };
            AutomationProperties.SetName(border, $"{seg.Label}, {seg.Type}, {seg.SizeText}");
            AutomationProperties.SetHelpText(border, $"Partition segment: {seg.Label}, type {seg.Type}, size {seg.SizeText}");

            var contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var accent = new Border
            {
                Background = fillBrush,
                CornerRadius = new CornerRadius(4, 0, 0, 4)
            };
            Grid.SetColumn(accent, 0);
            contentGrid.Children.Add(accent);

            var textStack = new StackPanel
            {
                Margin = new Thickness(9, 8, 8, 7),
                VerticalAlignment = VerticalAlignment.Center
            };
            textStack.Children.Add(new TextBlock
            {
                Text = seg.Label,
                Foreground = segmentTextBrush,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            textStack.Children.Add(new TextBlock
            {
                Text = seg.SizeText,
                Foreground = segmentSubtextBrush,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            if (seg.Proportion >= 0.055)
            {
                textStack.Children.Add(new TextBlock
                {
                    Text = seg.Type,
                    Foreground = segmentMutedBrush,
                    FontSize = 10.5,
                    Margin = new Thickness(0, 3, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            Grid.SetColumn(textStack, 1);
            contentGrid.Children.Add(textStack);

            border.Child = contentGrid;
            Grid.SetColumn(border, col);
            BarGrid.Children.Add(border);
            col++;
        }
    }
}
