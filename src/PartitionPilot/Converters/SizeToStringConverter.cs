using System.Globalization;
using System.Windows.Data;

namespace PartitionPilot;

public class SizeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes)
            return SizeUtil.Format(bytes);

        if (value is int intBytes)
            return SizeUtil.Format(intBytes);

        return "—";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
