using System.Globalization;
using System.Windows.Data;

namespace PartitionPilot;

public class ProportionToWidthConverter : IValueConverter
{
    public double MaxWidth { get; set; } = 140;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double proportion)
            return Math.Max(0, Math.Min(MaxWidth, proportion * MaxWidth));
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
