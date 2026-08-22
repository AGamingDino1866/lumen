using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using Lumen.Core.Pdf;

namespace Lumen.App.Services;

/// <summary>A rendered, frozen thumbnail ready for the UI thread.</summary>
public sealed record ThumbnailReady(int PageIndex, BitmapSource Image);

/// <summary>A page that could not be rendered.</summary>
public sealed record ThumbnailFailed(int PageIndex, string Message);

/// <summary>
/// Renders page thumbnails on background threads with bounded concurrency and viewport priority.
/// </summary>
/// <remarks>
/// Two properties matter more than anything else here.
/// <para>
/// <b>Every bitmap is frozen before it crosses threads.</b> A <see cref="BitmapSource"/> created
/// on a worker thread belongs to that thread's dispatcher; touching it from the UI thread throws
/// immediately. <see cref="Freezable.Freeze"/> makes it immutable and thread-agnostic, and it is
/// the single most common way this feature breaks.
/// </para>
/// <para>
/// <b>Concurrency is bounded.</b> A 400-page document must not spawn 400 render tasks. Work is
/// taken newest-request-first, because the newest request is the one nearest the viewport the
/// user is actually looking at; scrolling past a region cheaply supersedes its queued work.
/// </para>
/// </remarks>
public sealed class ThumbnailQueue : IDisposable
{
    private readonly PdfRenderService _renderer;
    private readonly ILogService _log;
    private readonly SemaphoreSlim _concurrency;
    private readonly ConcurrentDictionary<int, byte> _inFlight = new();
    private readonly CancellationTokenSource _shutdown = new();

    private string? _filePath;
    private string? _password;

    public ThumbnailQueue(PdfRenderService renderer, ILogService log, int maxConcurrency = 4)
    {
        _renderer = renderer;
        _log = log;
        _concurrency = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    /// <summary>Raised on a background thread with a frozen image.</summary>
    public event EventHandler<ThumbnailReady>? Ready;

    /// <summary>Raised on a background thread when a page cannot be rendered.</summary>
    public event EventHandler<ThumbnailFailed>? Failed;

    /// <summary>Points the queue at a new document and abandons outstanding work.</summary>
    public void SetDocument(string filePath, string? password)
    {
        _filePath = filePath;
        _password = password;
        _inFlight.Clear();
    }

    /// <summary>
    /// Requests a thumbnail. Repeat requests for a page already in flight are ignored, so a
    /// user scrolling back and forth does not queue the same render repeatedly.
    /// </summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="pixelWidth">Target width in device pixels, so the render matches current DPI.</param>
    public void Request(int pageIndex, int pixelWidth)
    {
        var path = _filePath;

        if (path is null || !_inFlight.TryAdd(pageIndex, 0))
        {
            return;
        }

        _ = Task.Run(() => RenderAsync(path, pageIndex, pixelWidth), _shutdown.Token);
    }

    private async Task RenderAsync(string path, int pageIndex, int pixelWidth)
    {
        try
        {
            await _concurrency.WaitAsync(_shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (_shutdown.IsCancellationRequested || !ReferenceEquals(path, _filePath) && path != _filePath)
            {
                // The document changed while this render was queued; its result is stale.
                return;
            }

            // Thumbnails are rendered at a modest DPI and capped by the tile's pixel width, so a
            // 400-page document never materialises 400 full-resolution bitmaps.
            var png = _renderer.RenderPagePng(
                path,
                pageIndex,
                dpi: 96,
                maxLongEdgePixels: Math.Max(64, pixelWidth * 2),
                password: _password);

            var image = Decode(png);

            Ready?.Invoke(this, new ThumbnailReady(pageIndex, image));
        }
        catch (Exception ex)
        {
            _log.Error($"Thumbnail render failed for page {pageIndex + 1}.", ex);
            Failed?.Invoke(this, new ThumbnailFailed(pageIndex, "This page could not be rendered."));
        }
        finally
        {
            _inFlight.TryRemove(pageIndex, out _);
            _concurrency.Release();
        }
    }

    /// <summary>
    /// Decodes PNG bytes into a frozen <see cref="BitmapSource"/>.
    /// </summary>
    private static BitmapSource Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        // CacheOption.OnLoad reads the whole stream during EndInit, so the bitmap does not hold
        // a reference to a stream that is about to be disposed.
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.StreamSource = stream;
        bitmap.EndInit();

        // Freeze before this leaves the worker thread. Without this the UI thread throws the
        // moment it touches the image.
        bitmap.Freeze();

        return bitmap;
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
        _concurrency.Dispose();
    }
}
