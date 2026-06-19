using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PartitionPilot.Controls;

public class TreemapControl : FrameworkElement
{
    public TreemapControl()
    {
        Cursor = Cursors.Hand;
        Focusable = true;
        KeyboardNavigation.SetIsTabStop(this, true);
    }

    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(IReadOnlyList<TreemapItem>),
            typeof(TreemapControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(TreemapItem),
            typeof(TreemapControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<TreemapItem>? Items
    {
        get => (IReadOnlyList<TreemapItem>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public TreemapItem? SelectedItem
    {
        get => (TreemapItem?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public event EventHandler<TreemapItem>? ItemClicked;

    private static readonly Brush[] Palette =
    {
        new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF)),
        new SolidColorBrush(Color.FromRgb(0x5E, 0xE0, 0xA0)),
        new SolidColorBrush(Color.FromRgb(0xF4, 0xC9, 0x6A)),
        new SolidColorBrush(Color.FromRgb(0xB1, 0x8C, 0xFF)),
        new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x6B)),
        new SolidColorBrush(Color.FromRgb(0x6B, 0xD4, 0xFF)),
        new SolidColorBrush(Color.FromRgb(0xA0, 0xE0, 0x5E)),
        new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
    };

    private static readonly Brush SelectedStroke = new SolidColorBrush(Colors.White);
    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(0x11, 0x13, 0x15));
    private static readonly Pen BorderPen = new(new SolidColorBrush(Color.FromRgb(0x20, 0x24, 0x2A)), 1);
    private static readonly Pen SelectedPen = new(SelectedStroke, 2);
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private List<(Rect Bounds, TreemapItem Item)> _layout = new();

    static TreemapControl()
    {
        foreach (var b in Palette) b.Freeze();
        SelectedStroke.Freeze();
        LabelBrush.Freeze();
        BorderPen.Freeze();
        SelectedPen.Freeze();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var items = Items;
        _layout.Clear();
        if (items is null || items.Count == 0 || ActualWidth < 1 || ActualHeight < 1)
            return;

        var sorted = items.Where(i => i.Size > 0).OrderByDescending(i => i.Size).ToList();
        if (sorted.Count == 0) return;

        var rects = Squarify(sorted, new Rect(0, 0, ActualWidth, ActualHeight));

        for (int i = 0; i < rects.Count; i++)
        {
            var rect = rects[i];
            var item = sorted[i];
            _layout.Add((rect, item));

            var brush = Palette[i % Palette.Length];
            var isSelected = item == SelectedItem;
            var pen = isSelected ? (IsKeyboardFocused ? GetFocusPen() : SelectedPen) : BorderPen;

            dc.DrawRectangle(brush, pen, rect);

            if (rect.Width > 40 && rect.Height > 16)
            {
                var label = item.Label;
                if (label.Length > 20) label = label[..17] + "...";
                var text = new FormattedText(label, CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, LabelTypeface, 11, LabelBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = rect.Width - 4 };
                dc.DrawText(text, new Point(rect.X + 3, rect.Y + 2));
            }

            if (rect.Width > 50 && rect.Height > 32)
            {
                var sizeText = new FormattedText(SizeUtil.Format(item.Size),
                    CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface,
                    10, LabelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
                    { MaxTextWidth = rect.Width - 4 };
                dc.DrawText(sizeText, new Point(rect.X + 3, rect.Y + 16));
            }
        }

        if (IsKeyboardFocused && ActualWidth > 3 && ActualHeight > 3)
            dc.DrawRectangle(null, GetFocusPen(), new Rect(1, 1, ActualWidth - 2, ActualHeight - 2));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var pos = e.GetPosition(this);
        foreach (var (bounds, item) in _layout)
        {
            if (bounds.Contains(pos))
            {
                SelectItem(item);
                break;
            }
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_layout.Count == 0) { base.OnKeyDown(e); return; }

        var currentIndex = SelectedItem is not null
            ? _layout.FindIndex(l => l.Item == SelectedItem)
            : -1;

        int newIndex = e.Key switch
        {
            Key.Right or Key.Down => Math.Min(currentIndex + 1, _layout.Count - 1),
            Key.Left or Key.Up => Math.Max(currentIndex - 1, 0),
            Key.Home => 0,
            Key.End => _layout.Count - 1,
            Key.Enter or Key.Space => currentIndex >= 0 ? currentIndex : 0,
            _ => -2
        };

        if (newIndex >= 0)
        {
            SelectItem(_layout[newIndex].Item);
            e.Handled = true;
        }
        else
        {
            base.OnKeyDown(e);
        }
    }

    private void SelectItem(TreemapItem item)
    {
        SelectedItem = item;
        ItemClicked?.Invoke(this, item);
        InvalidateVisual();

        var peer = UIElementAutomationPeer.FromElement(this);
        peer?.RaiseAutomationEvent(AutomationEvents.SelectionItemPatternOnElementSelected);
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new TreemapAutomationPeer(this);

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    private Pen GetFocusPen()
    {
        var brush = TryFindResource("PrimaryBrush") as Brush ?? Brushes.DodgerBlue;
        return new Pen(brush, 2) { DashStyle = DashStyles.Dash };
    }

    private static List<Rect> Squarify(List<TreemapItem> items, Rect bounds)
    {
        var result = new List<Rect>(items.Count);
        SquarifyRecursive(items, 0, items.Count, bounds, result);
        return result;
    }

    private static void SquarifyRecursive(List<TreemapItem> items, int start, int end, Rect bounds, List<Rect> result)
    {
        if (start >= end || bounds.Width < 1 || bounds.Height < 1)
        {
            for (int i = start; i < end; i++)
                result.Add(Rect.Empty);
            return;
        }

        if (end - start == 1)
        {
            result.Add(bounds);
            return;
        }

        double totalSize = 0;
        for (int i = start; i < end; i++)
            totalSize += items[i].Size;

        if (totalSize <= 0)
        {
            for (int i = start; i < end; i++)
                result.Add(Rect.Empty);
            return;
        }

        bool vertical = bounds.Width >= bounds.Height;
        double side = vertical ? bounds.Height : bounds.Width;

        double bestAspect = double.MaxValue;
        int bestSplit = start + 1;
        double runningSize = 0;

        for (int i = start; i < end; i++)
        {
            runningSize += items[i].Size;
            double fraction = runningSize / totalSize;
            double stripSize = vertical ? bounds.Width * fraction : bounds.Height * fraction;
            if (stripSize < 1) continue;

            double worstAspect = 0;
            double innerRunning = 0;
            for (int j = start; j <= i; j++)
            {
                innerRunning += items[j].Size;
                double h = side * (items[j].Size / runningSize);
                if (h < 1) continue;
                double aspect = Math.Max(stripSize / h, h / stripSize);
                if (aspect > worstAspect) worstAspect = aspect;
            }

            if (worstAspect < bestAspect)
            {
                bestAspect = worstAspect;
                bestSplit = i + 1;
            }
            else if (worstAspect > bestAspect * 1.5)
            {
                break;
            }
        }

        double splitFraction = 0;
        for (int i = start; i < bestSplit; i++)
            splitFraction += items[i].Size;
        splitFraction /= totalSize;

        Rect stripRect, remaining;
        if (vertical)
        {
            double w = bounds.Width * splitFraction;
            stripRect = new Rect(bounds.X, bounds.Y, w, bounds.Height);
            remaining = new Rect(bounds.X + w, bounds.Y, bounds.Width - w, bounds.Height);
        }
        else
        {
            double h = bounds.Height * splitFraction;
            stripRect = new Rect(bounds.X, bounds.Y, bounds.Width, h);
            remaining = new Rect(bounds.X, bounds.Y + h, bounds.Width, bounds.Height - h);
        }

        // Layout items in the strip
        double stripTotal = 0;
        for (int i = start; i < bestSplit; i++)
            stripTotal += items[i].Size;

        double offset = 0;
        for (int i = start; i < bestSplit; i++)
        {
            double frac = stripTotal > 0 ? items[i].Size / stripTotal : 0;
            if (vertical)
            {
                double h = stripRect.Height * frac;
                result.Add(new Rect(stripRect.X, stripRect.Y + offset, stripRect.Width, h));
                offset += h;
            }
            else
            {
                double w = stripRect.Width * frac;
                result.Add(new Rect(stripRect.X + offset, stripRect.Y, w, stripRect.Height));
                offset += w;
            }
        }

        SquarifyRecursive(items, bestSplit, end, remaining, result);
    }
}

public class TreemapItem
{
    public string Label { get; set; } = "";
    public long Size { get; set; }
    public string Path { get; set; } = "";
}

public class TreemapAutomationPeer(TreemapControl owner) : FrameworkElementAutomationPeer(owner)
{
    protected override string GetClassNameCore() => "TreemapControl";
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;
    protected override string GetHelpTextCore() => "Use arrow keys to move through folders and Enter or Space to select.";
    protected override string GetLocalizedControlTypeCore() => "disk usage treemap";

    protected override string GetNameCore()
    {
        var control = (TreemapControl)Owner;
        var selected = control.SelectedItem;
        if (selected is not null)
            return $"Disk usage treemap, selected: {selected.Label} ({SizeUtil.Format(selected.Size)})";
        return "Disk usage treemap";
    }
}
