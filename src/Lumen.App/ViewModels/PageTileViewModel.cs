using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lumen.App.ViewModels;

/// <summary>One page in the thumbnail grid.</summary>
public sealed partial class PageTileViewModel : ObservableObject
{
    public PageTileViewModel(int pageIndex, double aspectRatio)
    {
        PageIndex = pageIndex;
        AspectRatio = aspectRatio <= 0 ? 0.707 : aspectRatio;
    }

    /// <summary>Zero-based index into the document.</summary>
    public int PageIndex { get; }

    /// <summary>1-based number shown to the user.</summary>
    public int PageNumber => PageIndex + 1;

    /// <summary>
    /// Width divided by height. Held on the view model so the placeholder can reserve the exact
    /// final size, which is what stops the grid reflowing and the scroll position jumping when
    /// a thumbnail arrives.
    /// </summary>
    public double AspectRatio { get; }

    /// <summary>The rendered thumbnail. Always a frozen bitmap. Null until it arrives.</summary>
    [ObservableProperty]
    private BitmapSource? _thumbnail;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isRenderFailed;

    [ObservableProperty]
    private string? _renderError;

    /// <summary>
    /// True when the selection change came from a pointer, which is the only case where the
    /// check badge animates. Keyboard-initiated selection (Ctrl+A over 312 pages, Space, arrow
    /// keys) sets this false so no storyboard runs at all.
    /// </summary>
    [ObservableProperty]
    private bool _animateSelection;

    /// <summary>Read by Narrator, so a screen reader announces "Page 4, selected".</summary>
    public string AutomationName => IsSelected
        ? $"Page {PageNumber}, selected"
        : $"Page {PageNumber}, not selected";

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(AutomationName));

    /// <summary>Selects or deselects without triggering the badge animation.</summary>
    public void SetSelectedSilently(bool selected)
    {
        AnimateSelection = false;
        IsSelected = selected;
    }

    /// <summary>Selects or deselects from a pointer gesture, animating the badge.</summary>
    public void SetSelectedFromPointer(bool selected)
    {
        AnimateSelection = true;
        IsSelected = selected;
    }
}
