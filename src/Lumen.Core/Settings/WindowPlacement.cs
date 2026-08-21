namespace Lumen.Core.Settings;

/// <summary>
/// A screen-space rectangle. Deliberately not <c>System.Windows.Rect</c>, so this logic stays
/// in the WPF-free core and remains unit-testable.
/// </summary>
public readonly record struct Rect2D(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public double Area => Width <= 0 || Height <= 0 ? 0 : Width * Height;
}

/// <summary>
/// Decides whether a saved window rectangle is still usable on the currently connected monitors.
/// </summary>
/// <remarks>
/// This exists for the unplugged-second-monitor case: a window saved at x=3000 on a two-monitor
/// setup would otherwise restore entirely off-screen on a laptop, leaving the user with a running
/// process and no visible window.
/// </remarks>
public static class WindowPlacement
{
    /// <summary>
    /// True when at least <paramref name="minVisibleFraction"/> of the saved rectangle overlaps
    /// the union of the supplied monitor work areas.
    /// </summary>
    /// <param name="saved">The rectangle restored from settings.</param>
    /// <param name="monitorWorkAreas">Work areas of every currently connected monitor.</param>
    /// <param name="minVisibleFraction">
    /// How much of the window must be reachable. A few pixels of overlap is technically visible
    /// but not usable, so the default requires a quarter of the window to be on screen.
    /// </param>
    public static bool IsRectVisible(
        Rect2D saved,
        IReadOnlyList<Rect2D> monitorWorkAreas,
        double minVisibleFraction = 0.25)
    {
        if (saved.Area <= 0 || monitorWorkAreas.Count == 0)
        {
            return false;
        }

        // Monitor work areas do not overlap on Windows, so summing per-monitor intersections
        // gives the total visible area without double counting.
        var visibleArea = 0d;
        foreach (var monitor in monitorWorkAreas)
        {
            visibleArea += IntersectionArea(saved, monitor);
        }

        return visibleArea / saved.Area >= minVisibleFraction;
    }

    private static double IntersectionArea(Rect2D a, Rect2D b)
    {
        var width = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        var height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);

        return width <= 0 || height <= 0 ? 0 : width * height;
    }
}
