using System.Runtime.Versioning;
using FluentAssertions;
using Lumen.Core.Pdf;
using Xunit;

namespace Lumen.Core.Tests.Pdf;

/// <summary>
/// Exercises real PDFium rendering against a committed 3-page fixture. If these fail with a
/// native load error, the PDFium or SkiaSharp native assets are missing from the output folder,
/// which is the same failure the published single-file build must be checked for.
/// </summary>
[SupportedOSPlatform("windows")]
public class PdfRenderServiceTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.pdf");

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];

    private readonly PdfRenderService _service = new();

    [Fact]
    public void Fixture_is_present()
    {
        File.Exists(FixturePath).Should().BeTrue(
            "the fixture is copied to the output directory by the csproj None/CopyToOutputDirectory item");
    }

    [Fact]
    public void Open_reports_the_page_count()
    {
        _service.Open(FixturePath).PageCount.Should().Be(3);
    }

    [Fact]
    public void Open_reports_the_file_name_and_size()
    {
        var info = _service.Open(FixturePath);

        info.FileName.Should().Be("sample.pdf");
        info.FileSizeBytes.Should().BeGreaterThan(0);
        info.DisplaySize.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Open_reports_a_size_for_every_page()
    {
        var info = _service.Open(FixturePath);

        info.PageSizes.Should().HaveCount(3);
        info.PageSizes.Should().OnlyContain(s => s.Width > 0 && s.Height > 0);
    }

    [Fact]
    public void Open_reports_a4_geometry()
    {
        var first = _service.Open(FixturePath).PageSizes[0];

        first.Width.Should().BeApproximately(595, 2);
        first.Height.Should().BeApproximately(842, 2);
        first.AspectRatio.Should().BeLessThan(1, "A4 portrait is taller than it is wide");
    }

    [Fact]
    public void Open_throws_for_a_missing_file()
    {
        var act = () => _service.Open(Path.Combine(AppContext.BaseDirectory, "nope.pdf"));

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Open_throws_a_typed_error_for_a_non_pdf()
    {
        var junk = Path.Combine(Path.GetTempPath(), $"lumen-not-a-pdf-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(junk, "this is definitely not a pdf");

        try
        {
            var act = () => _service.Open(junk);

            act.Should().Throw<PdfUnreadableException>();
        }
        finally
        {
            File.Delete(junk);
        }
    }

    [Fact]
    public void Renders_a_page_as_png_bytes()
    {
        var png = _service.RenderPagePng(FixturePath, pageIndex: 0);

        png.Should().NotBeEmpty();
        png.Take(4).Should().Equal(PngMagic, "the result must be a real PNG");
    }

    [Fact]
    public void Renders_every_page()
    {
        for (var i = 0; i < 3; i++)
        {
            _service.RenderPagePng(FixturePath, i).Should().NotBeEmpty($"page {i + 1} must render");
        }
    }

    [Fact]
    public void Respects_the_long_edge_cap()
    {
        var png = _service.RenderPagePng(FixturePath, 0, dpi: 600, maxLongEdgePixels: 400);

        var (width, height) = ReadPngSize(png);

        Math.Max(width, height).Should().BeLessThanOrEqualTo(400,
            "an uncapped 600 DPI A4 render would exceed Gemini's payload limits");
    }

    [Fact]
    public void Higher_dpi_produces_a_larger_image()
    {
        var (lowWidth, _) = ReadPngSize(_service.RenderPagePng(FixturePath, 0, dpi: 72, maxLongEdgePixels: 4000));
        var (highWidth, _) = ReadPngSize(_service.RenderPagePng(FixturePath, 0, dpi: 200, maxLongEdgePixels: 4000));

        highWidth.Should().BeGreaterThan(lowWidth);
    }

    [Fact]
    public void Preserves_aspect_ratio_when_capping()
    {
        var (width, height) = ReadPngSize(_service.RenderPagePng(FixturePath, 0, dpi: 600, maxLongEdgePixels: 500));

        var rendered = (double)width / height;
        var expected = 595.0 / 842.0;

        rendered.Should().BeApproximately(expected, 0.02);
    }

    [Fact]
    public void Throws_for_a_page_index_beyond_the_document()
    {
        var act = () => _service.RenderPagePng(FixturePath, pageIndex: 99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Throws_for_a_negative_page_index()
    {
        var act = () => _service.RenderPagePng(FixturePath, pageIndex: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Near_blank_page_still_renders()
    {
        // Page 3 of the fixture is deliberately almost empty.
        _service.RenderPagePng(FixturePath, pageIndex: 2).Should().NotBeEmpty();
    }

    /// <summary>Reads width and height from the PNG IHDR chunk (bytes 16-23, big-endian).</summary>
    private static (int Width, int Height) ReadPngSize(byte[] png)
    {
        png.Length.Should().BeGreaterThan(24);

        var width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        var height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];

        return (width, height);
    }
}
