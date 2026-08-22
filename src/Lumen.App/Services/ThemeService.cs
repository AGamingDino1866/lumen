using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;

namespace Lumen.App.Services;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public interface IThemeService
{
    ThemeMode Current { get; }
    bool IsDark { get; }
    void Apply(ThemeMode mode);
    event EventHandler? ThemeChanged;
}

/// <summary>
/// Swaps the semantic token dictionary, which is the only layer that varies by theme.
/// </summary>
/// <remarks>
/// Primitives, components, motion, and icons are theme-invariant and stay merged for the
/// lifetime of the application; only the semantic dictionary at a known index is replaced. That
/// is what makes a theme switch a single-layer change rather than a reload of every style.
/// </remarks>
public sealed class ThemeService : IThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string LightThemeValue = "AppsUseLightTheme";

    private static readonly Uri LightUri = new("Themes/Semantic.Light.xaml", UriKind.Relative);
    private static readonly Uri DarkUri = new("Themes/Semantic.Dark.xaml", UriKind.Relative);

    /// <summary>A key that exists only in the semantic dictionaries, used to locate them.</summary>
    private const string SemanticMarkerKey = "Lumen.Surface.Canvas";

    private readonly Collection<ResourceDictionary> _dictionaries;
    private readonly int _semanticIndex;

    /// <param name="dictionaries">Application.Resources.MergedDictionaries.</param>
    /// <remarks>
    /// The semantic dictionary is located by a marker key rather than by a hardcoded index, so
    /// adding or reordering the WPF-UI dictionaries cannot silently break theme switching.
    /// </remarks>
    public ThemeService(Collection<ResourceDictionary> dictionaries)
    {
        _dictionaries = dictionaries;
        _semanticIndex = FindSemanticIndex(dictionaries);

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private static int FindSemanticIndex(Collection<ResourceDictionary> dictionaries)
    {
        for (var i = 0; i < dictionaries.Count; i++)
        {
            if (dictionaries[i].Contains(SemanticMarkerKey))
            {
                return i;
            }
        }

        throw new InvalidOperationException(
            $"No merged dictionary defines '{SemanticMarkerKey}'. App.xaml must merge Semantic.Light.xaml.");
    }

    public ThemeMode Current { get; private set; } = ThemeMode.System;

    public bool IsDark { get; private set; }

    public event EventHandler? ThemeChanged;

    public void Apply(ThemeMode mode)
    {
        Current = mode;

        var dark = mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            _ => IsSystemDark()
        };

        _dictionaries[_semanticIndex] = new ResourceDictionary
        {
            Source = dark ? DarkUri : LightUri
        };

        IsDark = dark;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reads the Windows app theme. Defaults to light when the value is unreadable.</summary>
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            // The value is "apps use LIGHT theme", so 0 means dark.
            return key?.GetValue(LightThemeValue) is int light && light == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Whether the Windows "Transparency effects" setting is on.</summary>
    /// <remarks>
    /// Respected alongside reduced motion. A user who has turned transparency off has asked not
    /// to see translucent chrome, so Mica is not applied and a solid canvas is used instead.
    /// </remarks>
    public static bool IsTransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("EnableTransparency") is not int enabled || enabled != 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle
            && Current == ThemeMode.System)
        {
            // Follow the system only while the user has not chosen an explicit override.
            Application.Current?.Dispatcher.Invoke(() => Apply(ThemeMode.System));
        }
    }

    public static ThemeMode Parse(string? value) => value switch
    {
        "Light" => ThemeMode.Light,
        "Dark" => ThemeMode.Dark,
        _ => ThemeMode.System
    };
}
