using System.Windows;

namespace Lumen.App.Services;

public interface IMotionService
{
    bool AnimationsEnabled { get; }
    void Refresh();
}

/// <summary>
/// Honours the Windows "Show animations in Windows" accessibility setting.
/// </summary>
/// <remarks>
/// When animations are switched off, every <see cref="Duration"/> in <c>Motion.xaml</c> is
/// rewritten to zero. Because all motion in the application reads its timing from those tokens,
/// this one operation disables animation everywhere without a single conditional inside a view.
/// <para>
/// Rewriting durations rather than removing storyboards keeps the visual end state identical:
/// a badge that scales in over 120 ms simply appears at its final size instead.
/// </para>
/// </remarks>
public sealed class MotionService : IMotionService
{
    /// <summary>Keys in Motion.xaml whose values are durations.</summary>
    private static readonly string[] DurationKeys =
    [
        "Lumen.Duration.BadgeIn",
        "Lumen.Duration.BadgeOut",
        "Lumen.Duration.Press",
        "Lumen.Duration.Hover",
        "Lumen.Duration.ThumbnailFade",
        "Lumen.Duration.CopyMorph",
        "Lumen.Duration.OverlayIn",
        "Lumen.Duration.OverlayOut",
        "Lumen.Duration.ResultEnter",
        "Lumen.Duration.DialogIn",
        "Lumen.Duration.DialogOut"
    ];

    private readonly ResourceDictionary _motion;
    private readonly Dictionary<string, Duration> _original = new(StringComparer.Ordinal);

    public MotionService(ResourceDictionary motionDictionary)
    {
        _motion = motionDictionary;

        foreach (var key in DurationKeys)
        {
            if (_motion[key] is Duration duration)
            {
                _original[key] = duration;
            }
        }

        Refresh();
    }

    public bool AnimationsEnabled { get; private set; } = true;

    public void Refresh()
    {
        AnimationsEnabled = SystemParameters.ClientAreaAnimation && SystemParameters.MenuAnimation;

        foreach (var key in DurationKeys)
        {
            if (!_original.TryGetValue(key, out var original))
            {
                continue;
            }

            _motion[key] = AnimationsEnabled ? original : new Duration(TimeSpan.Zero);
        }

        // The copy confirmation hold is not an animation, it is how long the confirmation stays
        // legible. It is deliberately left alone: a user who disabled animations still needs to
        // see that the copy succeeded.
    }
}
