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
        var theme = isDark ? ApplicationTheme.Dark : ApplicationTheme.Light;

        try
        {
            if (!ThemeService.IsTransparencyEnabled() || !IsMicaSupported())
            {
                _log.Info(!ThemeService.IsTransparencyEnabled()
                    ? "Transparency effects are disabled; using a solid backdrop."
                    : "Mica is unavailable on this Windows version; using a solid backdrop.");

                // ApplicationThemeManager also drives WPF-UI's own chrome (the title bar in
                // particular) — skipping it here left the title bar on WPF-UI's default light
                // theme regardless of what Lumen's own dark/light tokens said. It must run
                // whether or not Mica itself is used.
                ApplicationThemeManager.Apply(theme, WindowBackdropType.None, updateAccent: false);
                ForceOpaqueCanvas(window);
                return false;
            }

            ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: false);
            var applied = WindowBackdrop.ApplyBackdrop(window, WindowBackdropType.Mica);

            if (!applied)
            {
                ForceOpaqueCanvas(window);
            }

            return applied;
        }
        catch (Exception ex)
        {
            // A backdrop is decoration. Never let it prevent the window from showing.
            _log.Error("Applying the Mica backdrop failed; falling back to a solid surface.", ex);
            ForceOpaqueCanvas(window);
            return false;
        }
    }

    /// <summary>
    /// Declaring <c>WindowBackdropType="Mica"</c> in XAML makes WPF-UI's <c>FluentWindow</c>
    /// make the window transparent up front so Mica can show through. When Mica then fails at
    /// the OS level (no real DWM composition, or transparency effects off, which is exactly the
    /// case this method is called from), nothing else restores an opaque background, and the
    /// window falls through to whatever is behind it rather than Lumen's own canvas colour. This
    /// re-applies the dynamic resource explicitly so a code-set opaque brush wins regardless.
    /// </summary>
    private static void ForceOpaqueCanvas(Window window) =>
        window.SetResourceReference(Window.BackgroundProperty, "Lumen.Surface.Canvas");

    /// <summary>Mica arrived in Windows 11, which reports build 22000 or later.</summary>
    private static bool IsMicaSupported() =>
        Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;
}
