using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Lumen.App.Services;

public interface IBackdropService
{
    bool TryApply(Window window, bool isDark);
}

/// <summary>
/// Applies a Mica backdrop where Windows supports it, and falls back to a solid canvas where
/// it does not.
/// </summary>
/// <remarks>
/// Two things are detected rather than assumed.
/// <para>
/// <b>OS support.</b> Mica is a Windows 11 feature. On Windows 10 the call is a no-op at best,
/// so the window keeps its opaque canvas rather than ending up transparent.
/// </para>
/// <para>
/// <b>User preference.</b> If the Windows "Transparency effects" setting is off, the user has
/// asked not to see translucent chrome. That is an accessibility preference in the same family
/// as reduced motion, and it is honoured here rather than overridden.
/// </para>
/// </remarks>
public sealed class BackdropService : IBackdropService
{
    private readonly ILogService _log;

    public BackdropService(ILogService log) => _log = log;

    public bool TryApply(Window window, bool isDark)
    {
        try
        {
            if (!ThemeService.IsTransparencyEnabled())
            {
                _log.Info("Transparency effects are disabled; using a solid backdrop.");
                return false;
            }

            if (!IsMicaSupported())
            {
                _log.Info("Mica is unavailable on this Windows version; using a solid backdrop.");
                return false;
            }

            ApplicationThemeManager.Apply(
                isDark ? ApplicationTheme.Dark : ApplicationTheme.Light,
                WindowBackdropType.Mica,
                updateAccent: false);

            return WindowBackdrop.ApplyBackdrop(window, WindowBackdropType.Mica);
        }
        catch (Exception ex)
        {
            // A backdrop is decoration. Never let it prevent the window from showing.
            _log.Error("Applying the Mica backdrop failed; falling back to a solid surface.", ex);
            return false;
        }
    }

    /// <summary>Mica arrived in Windows 11, which reports build 22000 or later.</summary>
    private static bool IsMicaSupported() =>
        Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;
}
