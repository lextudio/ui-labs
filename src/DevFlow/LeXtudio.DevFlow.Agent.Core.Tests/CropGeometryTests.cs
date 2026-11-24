using LeXtudio.DevFlow.Agent.Wpf;
using Xunit;

namespace LeXtudio.DevFlow.Agent.Core.Tests;

/// <summary>
/// The crop maths behind element screenshots on macOS. The measured case is the OpenDevelop start-page button:
/// a 1024x768 pt window captured at 2x (2048x1536 px) with a 28 pt title bar over a 740 pt content area, button
/// at (312, 346) in content coordinates, 73x18 pt, which must land at (624, 748) and be 146x36 px.
/// </summary>
public class CropGeometryTests
{
    [Fact]
    public void TitleBarIsWindowMinusContent()
    {
        Assert.Equal(28, CropGeometry.TitleBarHeight(768, 740));
    }

    [Theory]
    [InlineData(768, 0)]
    [InlineData(768, -1)]
    [InlineData(700, 740)]
    public void TitleBarIsZeroWhenContentIsUnknownOrLarger(double window, double content)
    {
        Assert.Equal(0, CropGeometry.TitleBarHeight(window, content));
    }

    [Fact]
    public void MeasuredButtonLandsOnItsRetinaPixels()
    {
        var titleBar = CropGeometry.TitleBarHeight(768, 740);

        var r = CropGeometry.ToPixels((312, 346 + titleBar, 73, 18), (1024, 768), 2048, 1536);

        Assert.Equal((624d, 748d, 146d, 36d), r);
    }

    [Fact]
    public void ScaleFollowsTheImageNotAFixedFactor()
    {
        var r = CropGeometry.ToPixels((10, 10, 20, 20), (100, 100), 100, 100);

        Assert.Equal((10d, 10d, 20d, 20d), r);
    }

    [Fact]
    public void RegionPartlyOutsideTheImageIsClamped()
    {
        var r = CropGeometry.ToPixels((-10, 90, 30, 30), (100, 100), 200, 200);

        Assert.Equal((0d, 180d, 40d, 20d), r);
    }

    [Theory]
    [InlineData(200, 200, 10, 10)]
    [InlineData(-50, 0, 10, 10)]
    [InlineData(0, 0, 0, 10)]
    [InlineData(0, 0, 10, 0)]
    public void EmptyOrFullyOutsideRegionGivesNull(double x, double y, double w, double h)
    {
        Assert.Null(CropGeometry.ToPixels((x, y, w, h), (100, 100), 100, 100));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    public void UnknownWindowSizeGivesNull(double w, double h)
    {
        Assert.Null(CropGeometry.ToPixels((0, 0, 10, 10), (w, h), 100, 100));
    }
}
