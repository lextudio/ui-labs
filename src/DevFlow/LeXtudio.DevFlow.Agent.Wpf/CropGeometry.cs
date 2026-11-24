namespace LeXtudio.DevFlow.Agent.Wpf;

/// <summary>
/// Pure geometry for cropping an element out of a native window capture. Kept free of platform and WPF types so it can be
/// unit-tested on any host (it is linked into the Core test project).
/// </summary>
internal static class CropGeometry
{
    /// <summary>Height of the native title bar: the captured image includes it, element coordinates do not.</summary>
    public static double TitleBarHeight(double windowHeight, double contentHeight)
        => contentHeight > 0 ? Math.Max(0, windowHeight - contentHeight) : 0;

    /// <summary>
    /// Maps a region in window points (origin top-left) to image pixels, clamped to the image.
    /// Returns null when the window size is unknown or the clamped region is empty.
    /// </summary>
    public static (double X, double Y, double Width, double Height)? ToPixels(
        (double X, double Y, double Width, double Height) crop,
        (double Width, double Height) windowSize,
        double imageWidth,
        double imageHeight)
    {
        if (windowSize.Width <= 0 || windowSize.Height <= 0 || imageWidth <= 0 || imageHeight <= 0)
            return null;

        var scaleX = imageWidth / windowSize.Width;
        var scaleY = imageHeight / windowSize.Height;
        var x = Math.Clamp(crop.X * scaleX, 0, imageWidth);
        var y = Math.Clamp(crop.Y * scaleY, 0, imageHeight);
        var right = Math.Clamp((crop.X + crop.Width) * scaleX, 0, imageWidth);
        var bottom = Math.Clamp((crop.Y + crop.Height) * scaleY, 0, imageHeight);
        var width = right - x;
        var height = bottom - y;
        return width >= 1 && height >= 1 ? (x, y, width, height) : null;
    }
}
