using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Lumen.App.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Lumen.App.Views;

/// <summary>
/// Full-size page preview with arrow-key navigation and zoom.
/// </summary>
/// <remarks>
/// Renders at a higher DPI than the grid thumbnails, on a background thread, freezing the
/// bitmap before it reaches the UI thread exactly as the thumbnail queue does.
/// </remarks>
public partial class PagePreviewWindow : Window
{
    private const double MinZoom = 0.25;
    private const double MaxZoom = 5.0;

    private readonly MainViewModel _viewModel;
    private int _pageIndex;
    private CancellationTokenSource? _render;

    public PagePreviewWindow(MainViewModel viewModel, int pageIndex)
    {
        _viewModel = viewModel;
        _pageIndex = pageIndex;

        InitializeComponent();

        Loaded += async (_, _) => await LoadAsync();
        Closed += (_, _) => _render?.Cancel();
    }

    private int PageCount => _viewModel.Document?.PageCount ?? 0;

    private async Task LoadAsync()
    {
        _render?.Cancel();
        _render = new CancellationTokenSource();
        var token = _render.Token;

        LoadingText.Visibility = Visibility.Visible;
        PageLabel.Text = $"Page {_pageIndex + 1} of {PageCount}";
        UpdateZoomLabel();

        var document = _viewModel.Document;

        if (document is null)
        {
            return;
        }

        try
        {
            var image = await Task.Run(() =>
            {
                var png = _viewModel.RenderPreview(_pageIndex);

                using var stream = new MemoryStream(png);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();

                // Frozen on the worker thread, as everywhere else in Lumen.
                bitmap.Freeze();
                return (BitmapSource)bitmap;
            }, token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            PageImage.Source = image;
            LoadingText.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            LoadingText.Text = "This page could not be rendered.";
        }
    }

    private async void OnPrevious(object sender, RoutedEventArgs e) => await StepAsync(-1);

    private async void OnNext(object sender, RoutedEventArgs e) => await StepAsync(1);

    private async Task StepAsync(int delta)
    {
        var next = _pageIndex + delta;

        if (next < 0 || next >= PageCount)
        {
            return;
        }

        _pageIndex = next;
        await LoadAsync();
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                e.Handled = true;
                await StepAsync(-1);
                break;
            case Key.Right:
                e.Handled = true;
                await StepAsync(1);
                break;
            case Key.Escape:
                e.Handled = true;
                Close();
                break;
            default:
                base.OnKeyDown(e);
                break;
        }
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => ApplyZoom(Zoom.ScaleX * 1.25);

    private void OnZoomOut(object sender, RoutedEventArgs e) => ApplyZoom(Zoom.ScaleX / 1.25);

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Ctrl+wheel zooms; plain wheel keeps its normal scrolling behaviour.
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        e.Handled = true;
        ApplyZoom(Zoom.ScaleX * (e.Delta > 0 ? 1.15 : 1 / 1.15));
    }

    private void ApplyZoom(double scale)
    {
        var clamped = Math.Clamp(scale, MinZoom, MaxZoom);

        Zoom.ScaleX = clamped;
        Zoom.ScaleY = clamped;
        UpdateZoomLabel();
    }

    private void UpdateZoomLabel() => ZoomLabel.Text = $"{Zoom.ScaleX * 100:0}%";
}
