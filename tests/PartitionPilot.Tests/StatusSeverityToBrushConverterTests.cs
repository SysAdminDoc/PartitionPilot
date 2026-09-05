using System.Globalization;
using System.Windows.Media;

namespace PartitionPilot.Tests;

public class StatusSeverityToBrushConverterTests
{
    private readonly StatusSeverityToBrushConverter _converter = new();

    [Theory]
    [InlineData(StatusSeverity.Normal)]
    [InlineData(StatusSeverity.Warning)]
    [InlineData(StatusSeverity.Error)]
    public void Convert_AlwaysProducesABrush(StatusSeverity severity)
    {
        // No Application is running under the test host, so every lookup falls through to the fallback.
        // The contract that matters here is that the indicator never ends up with a null Fill.
        Assert.IsAssignableFrom<Brush>(_converter.Convert(severity, typeof(Brush), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Convert_HandlesAValueThatIsNotASeverity()
    {
        Assert.IsAssignableFrom<Brush>(_converter.Convert("nonsense", typeof(Brush), null!, CultureInfo.InvariantCulture));
        Assert.IsAssignableFrom<Brush>(_converter.Convert(null!, typeof(Brush), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_IsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack(Brushes.Red, typeof(StatusSeverity), null!, CultureInfo.InvariantCulture));
    }

}
