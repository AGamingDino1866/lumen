using System.Runtime.Versioning;
using PDFtoImage;
using SkiaSharp;

namespace Lumen.Core.Pdf;

/// <summary>Thrown when a PDF is encrypted and the supplied password was absent or wrong.</summary>
public sealed class PdfPasswordRequiredException : Exception
{
    public PdfPasswordRequiredException(string filePath)
        : base("This PDF is password protected.") => FilePath = filePath;

    public string FilePath { get; }
}

/// <summary>Thrown when a file is not a readable PDF.</summary>
public sealed class PdfUnreadableException : Exception
{
    public PdfUnreadableException(string filePath, Exception inner)
        : base("This file could not be opened as a PDF.", inner) => FilePath = filePath;

    public string FilePath { get; }
}

/// <summary>Metadata about an opened document.</summary>
public sealed record PdfDocumentInfo(
    string FilePath,
    string FileName,
    int PageCount,
    long FileSizeBytes,
    IReadOnlyList<PdfPageSize> PageSizes)
{
    /// <summary>Human-readable file size, e.g. "8.4 MB".</summary>
    public string DisplaySize => FileSizeBytes switch
    {
        < 1024 => $"{FileSizeBytes} B",
        < 1024 * 1024 => $"{FileSizeBytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{FileSizeBytes / (1024.0 * 1024):0.#} MB",
        _ => $"{FileSizeBytes / (1024.0 * 1024 * 1024):0.#} GB"
    };
}

/// <summary>Page dimensions in PDF points.</summary>
public readonly record struct PdfPageSize(double Width, double Height)
{
    public double AspectRatio => Height <= 0 ? 1 : Width / Height;
}

/// <summary>
/// Renders PDF pages to PNG bytes using PDFium via PDFtoImage.
/// </summary>
/// <remarks>
/// This service deliberately returns <see cref="byte"/> arrays rather than any WPF imaging type.
/// That is what keeps <c>Lumen.Core</c> free of WPF and lets these paths be tested without a UI
/// thread; the application layer is the single place that turns bytes into a
/// <c>BitmapSource</c> and freezes it.
/// <para>
/// The whole file is read into memory once and reused for every page render. PDFium reparses the
/// document per call either way, and holding the bytes avoids re-reading a large file from disk
/// once per thumbnail.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class PdfRenderService
{
    /// <summary>Opens a document and reads its metadata.</summary>
    /// <exception cref="PdfPasswordRequiredException">The document is encrypted.</exception>
    /// <exception cref="PdfUnreadableException">The file is not a readable PDF.</exception>
    public PdfDocumentInfo Open(string filePath, string? password = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The PDF could not be found.", filePath);
        }

        var bytes = File.ReadAllBytes(filePath);

        try
        {
            var pageCount = Conversion.GetPageCount(bytes, password);
            var sizes = Conversion.GetPageSizes(bytes, password)
                .Select(s => new PdfPageSize(s.Width, s.Height))
                .ToList();

            return new PdfDocumentInfo(
                filePath,
                Path.GetFileName(filePath),
                pageCount,
                bytes.LongLength,
                sizes);
        }
        catch (Exception ex) when (IsPasswordFailure(ex))
        {
            throw new PdfPasswordRequiredException(filePath);
        }
        catch (Exception ex) when (ex is not PdfPasswordRequiredException)
        {
            throw new PdfUnreadableException(filePath, ex);
        }
    }

    /// <summary>
    /// Renders one page to PNG bytes.
    /// </summary>
    /// <param name="filePath">Source document.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="dpi">Render resolution. 150-200 is legible for transcription.</param>
    /// <param name="maxLongEdgePixels">
    /// Upper bound on the longest edge. Caps the payload sent to Gemini: a large page at 200 DPI
    /// can otherwise exceed the request size limit.
    /// </param>
    /// <param name="password">Password for an encrypted document.</param>
    public byte[] RenderPagePng(
        string filePath,
        int pageIndex,
        int dpi = 150,
        int maxLongEdgePixels = 2048,
        string? password = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLongEdgePixels, 1);

        var bytes = File.ReadAllBytes(filePath);

        int pageCount;
        try
        {
            pageCount = Conversion.GetPageCount(bytes, password);
        }
        catch (Exception ex) when (IsPasswordFailure(ex))
        {
            throw new PdfPasswordRequiredException(filePath);
        }

        if (pageIndex >= pageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex), pageIndex, $"This document has {pageCount} pages.");
        }

        var options = BuildOptions(bytes, pageIndex, dpi, maxLongEdgePixels, password);

        using var bitmap = Conversion.ToImage(bytes, new Index(pageIndex), password, options);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, quality: 100);

        return data.ToArray();
    }

    /// <summary>
    /// Chooses render dimensions, scaling down when the requested DPI would exceed
    /// <paramref name="maxLongEdgePixels"/>.
    /// </summary>
    private static RenderOptions BuildOptions(
        byte[] bytes, int pageIndex, int dpi, int maxLongEdgePixels, string? password)
    {
        var size = Conversion.GetPageSizes(bytes, password)[pageIndex];

        // PDF user space is 72 points per inch.
        var widthPx = size.Width / 72.0 * dpi;
        var heightPx = size.Height / 72.0 * dpi;
        var longEdge = Math.Max(widthPx, heightPx);

        if (longEdge <= maxLongEdgePixels)
        {
            return new RenderOptions(Dpi: dpi, WithAspectRatio: true);
        }

        var scale = maxLongEdgePixels / longEdge;

        return new RenderOptions(
            Width: Math.Max(1, (int)Math.Round(widthPx * scale)),
            Height: Math.Max(1, (int)Math.Round(heightPx * scale)),
            WithAspectRatio: true);
    }

    /// <summary>
    /// PDFium surfaces a wrong or missing password as a generic exception whose message names
    /// the password, so the message is what has to be inspected.
    /// </summary>
    private static bool IsPasswordFailure(Exception ex) =>
        ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);
}
