using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Lumen.App.ViewModels;

namespace Lumen.App.Converters;

/// <summary>Visible when the bound value is true.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>Visible when the bound value is false. Avoids a negated property on every view model.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}

/// <summary>Visible when the bound value is neither null nor an empty string.</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            null => Visibility.Collapsed,
            string s => s.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
            _ => Visibility.Visible
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Inverts a boolean.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>
/// Derives a tile's height from its width and the page's aspect ratio.
/// </summary>
/// <remarks>
/// This is what lets the skeleton placeholder reserve exactly the space the finished thumbnail
/// will occupy, so the grid never reflows and the scroll position never jumps when a render
/// lands. Parameter is the tile width.
/// </remarks>
public sealed class AspectRatioToHeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var aspect = value is double d && d > 0 ? d : 0.707;
        var width = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
            ? w
            : 156d;

        return width / aspect;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps per-page extraction status onto its background token.</summary>
public sealed class StatusToBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            PageStatus.Queued => "Lumen.Status.Queued",
            PageStatus.Running => "Lumen.Status.Running",
            PageStatus.Done => "Lumen.Status.Done",
            PageStatus.Failed => "Lumen.Status.Failed",
            _ => "Lumen.Status.Queued"
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps per-page extraction status onto its foreground token.</summary>
public sealed class StatusToForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            PageStatus.Queued => "Lumen.Status.Queued.Fg",
            PageStatus.Running => "Lumen.Status.Running.Fg",
            PageStatus.Done => "Lumen.Status.Done.Fg",
            PageStatus.Failed => "Lumen.Status.Failed.Fg",
            _ => "Lumen.Status.Queued.Fg"
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
