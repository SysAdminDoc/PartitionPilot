using System.Windows;

namespace PartitionPilot.Tests;

public class ShellSettingsServiceTests
{
    private static readonly Rect PrimaryWorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void TryGetVisiblePlacement_RestoresAPlacementThatIsStillOnScreen()
    {
        var settings = new ShellSettings { WindowLeft = 100, WindowTop = 80, WindowWidth = 1380, WindowHeight = 860 };

        Assert.True(ShellSettingsService.TryGetVisiblePlacement(
            settings, PrimaryWorkArea, out var left, out var top, out var width, out var height));

        Assert.Equal(100, left);
        Assert.Equal(80, top);
        Assert.Equal(1380, width);
        Assert.Equal(860, height);
    }

    [Fact]
    public void TryGetVisiblePlacement_PullsAWindowBackFromAMonitorThatIsGone()
    {
        // Saved on a second monitor to the right that has since been unplugged. Restoring it verbatim
        // would open the window somewhere the user cannot reach it.
        var settings = new ShellSettings { WindowLeft = 3000, WindowTop = 200, WindowWidth = 1380, WindowHeight = 860 };

        Assert.True(ShellSettingsService.TryGetVisiblePlacement(
            settings, PrimaryWorkArea, out var left, out var top, out var width, out var height));

        Assert.InRange(left, PrimaryWorkArea.Left, PrimaryWorkArea.Right - width);
        Assert.InRange(top, PrimaryWorkArea.Top, PrimaryWorkArea.Bottom - height);
    }

    [Fact]
    public void TryGetVisiblePlacement_PullsBackANegativeOffScreenPosition()
    {
        var settings = new ShellSettings { WindowLeft = -4000, WindowTop = -900, WindowWidth = 1000, WindowHeight = 700 };

        Assert.True(ShellSettingsService.TryGetVisiblePlacement(
            settings, PrimaryWorkArea, out var left, out var top, out _, out _));

        Assert.Equal(PrimaryWorkArea.Left, left);
        Assert.Equal(PrimaryWorkArea.Top, top);
    }

    [Fact]
    public void TryGetVisiblePlacement_ShrinksAWindowLargerThanTheWorkArea()
    {
        var settings = new ShellSettings { WindowLeft = 0, WindowTop = 0, WindowWidth = 5000, WindowHeight = 3000 };

        Assert.True(ShellSettingsService.TryGetVisiblePlacement(
            settings, PrimaryWorkArea, out _, out _, out var width, out var height));

        Assert.Equal(PrimaryWorkArea.Width, width);
        Assert.Equal(PrimaryWorkArea.Height, height);
    }

    [Theory]
    [InlineData(null, null, 100d, 100d)]
    [InlineData(100d, 100d, null, null)]
    [InlineData(100d, 100d, 0d, 500d)]
    [InlineData(100d, 100d, 500d, -1d)]
    public void TryGetVisiblePlacement_RefusesAnIncompleteOrNonsensePlacement(
        double? left, double? top, double? width, double? height)
    {
        var settings = new ShellSettings
        {
            WindowLeft = left, WindowTop = top, WindowWidth = width, WindowHeight = height
        };

        Assert.False(ShellSettingsService.TryGetVisiblePlacement(settings, PrimaryWorkArea, out _, out _, out _, out _));
    }

    [Fact]
    public void TryGetVisiblePlacement_RefusesAFreshInstallWithNothingRemembered()
    {
        Assert.False(ShellSettingsService.TryGetVisiblePlacement(
            new ShellSettings(), PrimaryWorkArea, out _, out _, out _, out _));
    }
}
