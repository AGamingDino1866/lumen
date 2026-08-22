using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Lumen.App.Services;
using Lumen.App.ViewModels;
using Lumen.Core.Settings;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using ListViewItem = System.Windows.Controls.ListViewItem;
using Path = System.Windows.Shapes.Path;
using TextBox = System.Windows.Controls.TextBox;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using Screen = System.Windows.Forms.Screen;

namespace Lumen.App.Views;

/// <summary>
/// The shell. Code-behind here is strictly view concerns: window placement, drag-and-drop
/// plumbing, selection marshalling between the ListView and the view models, and the copy
/// confirmation animation. All logic lives in <see cref="MainViewModel"/>.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private readonly IBackdropService _backdrop;
    private readonly IThemeService _theme;
    private readonly ILogService _log;

    private bool _suppressSelectionSync;
    private readonly string? _pendingFilePath;

    public MainWindow(
        MainViewModel viewModel,
        IBackdropService backdrop,
        IThemeService theme,
        ILogService log,
        string? pendingFilePath = null)
    {
        _viewModel = viewModel;
        _backdrop = backdrop;
        _theme = theme;
        _log = log;
        _pendingFilePath = pendingFilePath;

        InitializeComponent();

        DataContext = viewModel;

        viewModel.PasswordRequired += OnPasswordRequired;
        viewModel.ExportCompleted += OnExportCompleted;
        _theme.ThemeChanged += OnThemeChanged;

        Loaded += OnLoaded;
    }

    // ===================== Startup =====================

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestorePlacement();
        RestoreSplitter();

        _backdrop.TryApply(this, _theme.IsDark);
        UpdateThemeIcon();

        // The key gate is a hard prerequisite: without a key the application cannot do the one
        // thing it exists for, so it blocks rather than deferring.
        if (!_viewModel.HasApiKey)
        {
            ShowApiKeyGate();
        }

        // A PDF passed on the command line (a double-click via the "Open with Lumen" file
        // association, or a path handed in by the FlaUI suite) opens only after the gate above
        // has resolved, since extraction needs a key regardless of how the document arrived.
        if (_pendingFilePath is { Length: > 0 } path && File.Exists(path))
        {
            _viewModel.LoadDocument(path);
        }

        PageGrid.Focus();
    }

    /// <summary>
    /// Restores the saved window rectangle only if it still lands on a connected monitor.
    /// </summary>
    private void RestorePlacement()
    {
        var settings = _viewModel.Settings;

        Width = Math.Max(MinWidth, settings.WindowWidth);
        Height = Math.Max(MinHeight, settings.WindowHeight);

        if (settings.WindowLeft is { } left && settings.WindowTop is { } top)
        {
            var monitors = Screen.AllScreens
                .Select(s => new Rect2D(
                    s.WorkingArea.Left, s.WorkingArea.Top, s.WorkingArea.Width, s.WorkingArea.Height))
                .ToList();

            if (WindowPlacement.IsRectVisible(new Rect2D(left, top, Width, Height), monitors))
            {
                Left = left;
                Top = top;
            }
            else
            {
                // The monitor it was saved on is gone. Centre rather than restoring off-screen,
                // which would leave a running process with no visible window.
                _log.Info("Saved window position is off-screen; centring on the primary display.");
                CenterOnPrimary();
            }
        }
        else
        {
            CenterOnPrimary();
        }

        if (settings.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void CenterOnPrimary()
    {
        var work = Screen.PrimaryScreen?.WorkingArea;

        if (work is null)
        {
            return;
        }

        Left = work.Value.Left + (work.Value.Width - Width) / 2;
        Top = work.Value.Top + (work.Value.Height - Height) / 2;
    }

    private void RestoreSplitter()
    {
        var fraction = Math.Clamp(_viewModel.Settings.SplitterPosition, 0.2, 0.8);

        GridColumn.Width = new GridLength(fraction, GridUnitType.Star);
        ResultsColumn.Width = new GridLength(1 - fraction, GridUnitType.Star);
    }

    // ===================== Shutdown =====================

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var settings = _viewModel.Settings;

        settings.Maximized = WindowState == WindowState.Maximized;

        // RestoreBounds carries the pre-maximise rectangle, which is what should be restored
        // next time; Left/Top while maximised would save the maximised position instead.
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        settings.WindowLeft = bounds.Left;
        settings.WindowTop = bounds.Top;
        settings.WindowWidth = bounds.Width;
        settings.WindowHeight = bounds.Height;

        var total = GridColumn.Width.Value + ResultsColumn.Width.Value;
        if (total > 0)
        {
            settings.SplitterPosition = GridColumn.Width.Value / total;
        }

        _viewModel.PersistSettings();
        _viewModel.Dispose();
    }

    // ===================== Theme =====================

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        _backdrop.TryApply(this, _theme.IsDark);
        UpdateThemeIcon();
    }

    private void UpdateThemeIcon()
    {
        // The icon shows the destination, not the current state: a sun means "switch to light".
        var key = _theme.IsDark ? "Lumen.Icon.Sun" : "Lumen.Icon.Moon";

        if (TryFindResource(key) is System.Windows.Media.Geometry geometry)
        {
            ThemeIcon.Data = geometry;
        }
    }

    // ===================== Drag and drop =====================

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        e.Effects = DragDropEffects.Copy;
        ShowDropOverlay(true);
    }

    private void OnDragLeave(object sender, DragEventArgs e) => ShowDropOverlay(false);

    private void OnDrop(object sender, DragEventArgs e)
    {
        ShowDropOverlay(false);

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files)
        {
            return;
        }

        _viewModel.LoadDocument(files[0]);
    }

    private void ShowDropOverlay(bool show)
    {
        var inDuration = (Duration)FindResource("Lumen.Duration.OverlayIn");
        var outDuration = (Duration)FindResource("Lumen.Duration.OverlayOut");

        if (show)
        {
            DropOverlay.Visibility = Visibility.Visible;
            DropOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, inDuration));
            DropCardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1, inDuration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            DropCardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, inDuration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            return;
        }

        // Exit faster than enter, and hide only once the fade has finished.
        var fade = new DoubleAnimation(0, outDuration);
        fade.Completed += (_, _) =>
        {
            DropOverlay.Visibility = Visibility.Collapsed;
            DropCardScale.ScaleX = 0.96;
            DropCardScale.ScaleY = 0.96;
        };

        DropOverlay.BeginAnimation(OpacityProperty, fade);
    }

    // ===================== Selection =====================

    /// <summary>
    /// Makes a plain click toggle that page and accumulate, with no modifier key needed.
    /// Extended selection's own default is to replace the whole selection on a plain click and
    /// only accumulate on Ctrl+Click; that default is suppressed here, and Shift+Click is left
    /// alone so a contiguous range still works the normal way.
    /// </summary>
    private void OnTilePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || sender is not ListViewItem item)
        {
            return;
        }

        e.Handled = true;

        if (PageGrid.SelectedItems.Contains(item.DataContext))
        {
            PageGrid.SelectedItems.Remove(item.DataContext);
        }
        else
        {
            PageGrid.SelectedItems.Add(item.DataContext);
        }

        item.Focus();
    }

    /// <summary>
    /// Mirrors the ListView's own Extended selection onto the view models.
    /// </summary>
    /// <remarks>
    /// Extended mode is what supplies Ctrl+Click and Shift+Click range behaviour, so it is used
    /// rather than reimplemented. The view models still carry IsSelected because the results
    /// panel, the page-range box, and the action bar all read selection independently of the
    /// control that produced it.
    /// </remarks>
    private void OnPageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionSync)
        {
            return;
        }

        var fromPointer = Mouse.LeftButton == MouseButtonState.Pressed
                          || Mouse.RightButton == MouseButtonState.Pressed;

        if (fromPointer && e.AddedItems.Count == 1 && e.AddedItems[0] is PageTileViewModel clicked)
        {
            ScrollResultIntoView(clicked.PageNumber);
        }

        foreach (var item in e.RemovedItems.OfType<PageTileViewModel>())
        {
            if (fromPointer)
            {
                item.SetSelectedFromPointer(false);
            }
            else
            {
                item.SetSelectedSilently(false);
            }
        }

        foreach (var item in e.AddedItems.OfType<PageTileViewModel>())
        {
            if (fromPointer)
            {
                item.SetSelectedFromPointer(true);
            }
            else
            {
                // Keyboard-initiated selection animates nothing. Ctrl+A across 312 pages must
                // not fire 312 storyboards.
                item.SetSelectedSilently(true);
            }
        }

        _viewModel.RecalculateSelection();
    }

    /// <summary>
    /// Scrolls the merged results flow so the given page's divider is visible. The panel is
    /// virtualized, so an off-screen item may not have a realized container yet: a first attempt
    /// jumps to an estimated offset, then retries once layout has run.
    /// </summary>
    private void ScrollResultIntoView(int pageNumber)
    {
        var result = _viewModel.Results.FirstOrDefault(r => r.PageNumber == pageNumber);
        if (result is null)
        {
            return;
        }

        if (ResultsItems.ItemContainerGenerator.ContainerFromItem(result) is FrameworkElement realized)
        {
            realized.BringIntoView();
            return;
        }

        var index = _viewModel.Results.IndexOf(result);
        if (index < 0 || _viewModel.Results.Count == 0 || ResultsScroller.ExtentHeight <= 0)
        {
            return;
        }

        var fraction = (double)index / _viewModel.Results.Count;
        ResultsScroller.ScrollToVerticalOffset(fraction * ResultsScroller.ExtentHeight);

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (ResultsItems.ItemContainerGenerator.ContainerFromItem(result) is FrameworkElement fe)
            {
                fe.BringIntoView();
            }
        }));
    }

    /// <summary>
    /// A click on a results divider selects and scrolls to the matching thumbnail. The
    /// selection-sync suppression prevents that programmatic selection from bouncing straight
    /// back into <see cref="ScrollResultIntoView"/> and re-scrolling the panel the user just
    /// clicked in.
    /// </summary>
    private void OnResultDividerClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PageResultViewModel result)
        {
            return;
        }

        var tile = _viewModel.Pages.FirstOrDefault(p => p.PageNumber == result.PageNumber);
        if (tile is null)
        {
            return;
        }

        _suppressSelectionSync = true;
        try
        {
            PageGrid.SelectedItem = tile;
            PageGrid.ScrollIntoView(tile);
        }
        finally
        {
            _suppressSelectionSync = false;
        }
    }

    // ===================== Find in results =====================

    private readonly List<PageResultViewModel> _findMatches = [];
    private int _findMatchIndex = -1;

    private void OnFindTextChanged(object sender, TextChangedEventArgs e)
    {
        _findMatches.Clear();
        _findMatchIndex = -1;

        var query = FindBox.Text;
        if (!string.IsNullOrWhiteSpace(query))
        {
            _findMatches.AddRange(_viewModel.Results.Where(r =>
                !string.IsNullOrEmpty(r.Markdown) &&
                r.Markdown.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        JumpToMatch(0);
    }

    private void OnFindBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        JumpToMatch(Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1);
    }

    /// <summary>
    /// Moves to the match <paramref name="delta"/> positions from the current one (0 re-selects
    /// the current match, used right after the query changes) and highlights it by selecting the
    /// matched substring in its result's TextBox — a plain read-only TextBox has no rich-text
    /// highlighting, but its selection brush renders identically and needs no extra control.
    /// </summary>
    private void JumpToMatch(int delta)
    {
        if (_findMatches.Count == 0)
        {
            return;
        }

        _findMatchIndex = ((_findMatchIndex + delta) % _findMatches.Count + _findMatches.Count) % _findMatches.Count;
        var result = _findMatches[_findMatchIndex];
        var query = FindBox.Text;

        ScrollResultIntoView(result.PageNumber);

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (ResultsItems.ItemContainerGenerator.ContainerFromItem(result) is not DependencyObject container)
            {
                return;
            }

            if (FindDescendant<TextBox>(container) is not { } textBox)
            {
                return;
            }

            var index = textBox.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                textBox.Select(index, query.Length);
            }
        }));
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    // ===================== Copy feedback =====================

    /// <summary>
    /// Opens the format-choice menu anchored to the small chevron button beside Copy All.
    /// A left click doesn't open a Button's ContextMenu on its own; that only happens on
    /// right-click unless told to.
    /// </summary>
    private void OnCopyAllFormatClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    /// <summary>
    /// The toolbar Copy All button morphs into a checkmark reading "Copied" for ~1.5s, then eases
    /// back. The actual clipboard write happens via the bound Command; this handles only the
    /// view-level confirmation, which is why both Click and Command are wired on the same button.
    /// </summary>
    private async void OnCopyAllClick(object sender, RoutedEventArgs e)
    {
        var token = new object();
        CopyAllButton.Tag = token;

        CopyAllIcon.Data = (Geometry)FindResource("Lumen.Icon.Check");
        CopyAllLabel.Text = "Copied";

        await Task.Delay(TimeSpan.FromSeconds(1.5));

        if (ReferenceEquals(CopyAllButton.Tag, token))
        {
            CopyAllIcon.Data = (Geometry)FindResource("Lumen.Icon.Copy");
            CopyAllLabel.Text = "Copy All";
        }
    }

    /// <summary>
    /// Per-page copy buttons get the same checkmark morph, scoped to that button's own icon so a
    /// copy on one page never disturbs another's feedback. The icon's Tag holds a fresh token per
    /// click so a rapid re-copy supersedes, rather than races, its own pending revert; this also
    /// behaves correctly when the results panel recycles the container for a different page.
    /// </summary>
    private async void OnCopyPageClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Content: Path icon })
        {
            return;
        }

        var token = new object();
        icon.Tag = token;

        var original = icon.Data;
        icon.Data = (Geometry)FindResource("Lumen.Icon.Check");

        await Task.Delay(TimeSpan.FromSeconds(1.5));

        if (ReferenceEquals(icon.Tag, token))
        {
            icon.Data = original;
        }
    }

    private void OnPageDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PageGrid.SelectedItem is not PageTileViewModel tile || _viewModel.Document is null)
        {
            return;
        }

        var preview = new PagePreviewWindow(_viewModel, tile.PageIndex) { Owner = this };
        preview.ShowDialog();
    }

    // ===================== Dialogs =====================

    private void ShowApiKeyGate()
    {
        var gate = new ApiKeyGateDialog(_viewModel) { Owner = this };

        if (gate.ShowDialog() != true)
        {
            // The application cannot function without a key, so declining closes it rather than
            // leaving the user in a window where every action fails.
            Close();
        }
    }

    /// <summary>
    /// Export options (page break / heading-per-page) are collected here rather than always
    /// taking Settings' fixed defaults, so the export dialog's checkboxes actually mean something
    /// on every export, not just the first one.
    /// </summary>
    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanExport)
        {
            return;
        }

        var dialog = new ExportDialog(_viewModel.Settings.ExportPageBreaks, _viewModel.Settings.ExportPageHeadings)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.Export(dialog.PageBreaks, dialog.PageHeadings);
        }
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(_viewModel, _theme) { Owner = this };
        settings.ShowDialog();

        if (!_viewModel.HasApiKey)
        {
            ShowApiKeyGate();
        }
    }

    private void OnPasswordRequired(object? sender, string path)
    {
        var prompt = new PasswordDialog(System.IO.Path.GetFileName(path)) { Owner = this };

        if (prompt.ShowDialog() == true)
        {
            _viewModel.LoadDocument(path, prompt.Password);
        }
    }

    private void OnExportCompleted(object? sender, string path)
    {
        var choice = MessageBox.Show(
            $"Exported to {System.IO.Path.GetFileName(path)}.\n\nOpen it now?",
            "Lumen",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.None);

        switch (choice)
        {
            case MessageBoxResult.Yes:
                new DialogService().OpenFile(path);
                break;
            case MessageBoxResult.No:
                new DialogService().ShowInFolder(path);
                break;
        }
    }

    /// <summary>
    /// Ctrl+F moves focus to the find box. Handled here rather than as a bound command because
    /// the target is a view element, which is a view concern and not the view model's business.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && _viewModel.HasResults)
        {
            FindBox.Focus();
            FindBox.SelectAll();
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }
}
