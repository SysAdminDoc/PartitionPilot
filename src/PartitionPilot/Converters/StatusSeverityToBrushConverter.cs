using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PartitionPilot;

/// <summary>
/// Maps a status severity to a themed brush.
/// <para>
/// Resolved at conversion time rather than bound to a fixed resource so the indicator follows a theme
/// change, the way the rest of the shell does.
/// </para>
/// </summary>
public class StatusSeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = (value as StatusSeverity?) switch
        {
            StatusSeverity.Error => "DangerBrush",
            StatusSeverity.Warning => "WarningBrush",
            _ => "SuccessBrush"
        };

        return Application.Current?.TryFindResource(key) as Brush
               ?? Application.Current?.TryFindResource("SuccessBrush") as Brush
               ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
