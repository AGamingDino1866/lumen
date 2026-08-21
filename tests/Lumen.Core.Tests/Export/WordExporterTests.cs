using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Lumen.Core.Export;
using Lumen.Core.Markdown;
using Xunit;

namespace Lumen.Core.Tests.Export;

public class WordExporterTests
{
    private static MemoryStream Export(string markdown, WordExportOptions? options = null)
    {
        var stream = new MemoryStream();
        WordExporter.Write(
            stream,
            [new ExportPage(1, MarkdownParser.Parse(markdown))],
            options ?? new WordExportOptions(PageBreakBetweenPages: false, PageHeadingPerPage: false));
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream ExportPages(WordExportOptions options, params (int Page, string Markdown)[] pages)
    {
        var stream = new MemoryStream();
        WordExporter.Write(
            stream,
            pages.Select(p => new ExportPage(p.Page, MarkdownParser.Parse(p.Markdown))).ToList(),
            options);
        stream.Position = 0;
        return stream;
    }

    private static Body BodyOf(MemoryStream stream) =>
        WordprocessingDocument.Open(stream, false).MainDocumentPart!.Document!.Body!;

    [Fact]
    public void Produces_a_file_word_can_open()
    {
        using var stream = Export("# Title");

        var act = () => WordprocessingDocument.Open(stream, false);

        act.Should().NotThrow("the output must be a real OpenXML package, not text with a .docx name");
    }

    [Theory]
    [InlineData("# H", "Heading1")]
    [InlineData("## H", "Heading2")]
    [InlineData("### H", "Heading3")]
    public void Headings_use_real_word_heading_styles(string markdown, string expectedStyle)
    {
        using var stream = Export(markdown);
        using var doc = WordprocessingDocument.Open(stream, false);

        doc.MainDocumentPart!.Document!.Body!
            .Descendants<Paragraph>()
            .Select(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value)
            .Should().Contain(expectedStyle,
                "Word's navigation pane and table of contents only work with real heading styles");
    }

    [Fact]
    public void Heading_styles_are_defined_in_the_styles_part()
    {
        using var stream = Export("# Title");
        using var doc = WordprocessingDocument.Open(stream, false);

        var styles = doc.MainDocumentPart!.StyleDefinitionsPart;

        styles.Should().NotBeNull("Word ignores a style id that the document does not define");
        styles!.Styles!.Descendants<Style>()
            .Select(s => s.StyleId?.Value)
            .Should().Contain("Heading1");
    }

    [Fact]
    public void Table_becomes_a_real_word_table()
    {
        const string md = """
            | Region | Revenue |
            | ------ | ------- |
            | North  | 1200    |
            """;

        using var stream = Export(md);
        using var doc = WordprocessingDocument.Open(stream, false);

        var table = doc.MainDocumentPart!.Document!.Body!.Descendants<Table>().Should().ContainSingle().Subject;

        table.Descendants<TableRow>().Should().HaveCount(2, "one header row plus one body row");
        table.Descendants<TableCell>().Should().HaveCount(4);
    }

    [Fact]
    public void Table_has_borders()
    {
        const string md = "| A | B |\n| - | - |\n| 1 | 2 |";

        using var stream = Export(md);
        using var doc = WordprocessingDocument.Open(stream, false);

        doc.MainDocumentPart!.Document!.Body!.Descendants<TableBorders>()
            .Should().NotBeEmpty();
    }

    [Fact]
    public void Table_header_row_is_styled_distinctly()
    {
        const string md = "| A | B |\n| - | - |\n| 1 | 2 |";

        using var stream = Export(md);
        using var doc = WordprocessingDocument.Open(stream, false);

        var firstRow = doc.MainDocumentPart!.Document!.Body!.Descendants<TableRow>().First();

        firstRow.Descendants<Bold>().Should().NotBeEmpty("the header row should read as a header");
    }

    [Fact]
    public void Bold_run_carries_bold_character_formatting()
    {
        using var stream = Export("plain **bold** plain");
        using var doc = WordprocessingDocument.Open(stream, false);

        var boldRun = doc.MainDocumentPart!.Document!.Body!.Descendants<Run>()
            .FirstOrDefault(r => r.RunProperties?.Bold is not null);

        boldRun.Should().NotBeNull();
        boldRun!.InnerText.Should().Be("bold");
    }

    [Fact]
    public void Italic_run_carries_italic_character_formatting()
    {
        using var stream = Export("an *italic* word");
        using var doc = WordprocessingDocument.Open(stream, false);

        doc.MainDocumentPart!.Document!.Body!.Descendants<Run>()
            .Where(r => r.RunProperties?.Italic is not null)
            .Select(r => r.InnerText)
            .Should().Contain("italic");
    }

    [Fact]
    public void Bulleted_list_uses_real_numbering()
    {
        using var stream = Export("- alpha\n- beta");
        using var doc = WordprocessingDocument.Open(stream, false);

        doc.MainDocumentPart!.NumberingDefinitionsPart
            .Should().NotBeNull("a real list needs a numbering part, not a literal hyphen");
        doc.MainDocumentPart.Document!.Body!.Descendants<NumberingProperties>()
            .Should().NotBeEmpty();
    }

    [Fact]
    public void Numbered_list_uses_real_numbering()
    {
        using var stream = Export("1. first\n2. second");
        using var doc = WordprocessingDocument.Open(stream, false);

        doc.MainDocumentPart!.Document!.Body!.Descendants<NumberingProperties>()
            .Should().NotBeEmpty();
    }

    [Fact]
    public void Inserts_a_page_break_between_pages_when_the_option_is_set()
    {
        using var stream = ExportPages(
            new WordExportOptions(PageBreakBetweenPages: true, PageHeadingPerPage: false),
            (1, "First"), (2, "Second"));

        BodyOf(stream).Descendants<Break>()
            .Count(b => b.Type is not null && b.Type == BreakValues.Page)
            .Should().Be(1, "a break belongs between the two pages, not after the last one");
    }

    [Fact]
    public void Omits_page_breaks_when_the_option_is_cleared()
    {
        using var stream = ExportPages(
            new WordExportOptions(PageBreakBetweenPages: false, PageHeadingPerPage: false),
            (1, "First"), (2, "Second"));

        BodyOf(stream).Descendants<Break>()
            .Count(b => b.Type is not null && b.Type == BreakValues.Page)
            .Should().Be(0);
    }

    [Fact]
    public void Emits_a_page_heading_when_the_option_is_set()
    {
        using var stream = ExportPages(
            new WordExportOptions(PageBreakBetweenPages: false, PageHeadingPerPage: true),
            (12, "Body text"));

        BodyOf(stream).InnerText.Should().Contain("Page 12");
    }

    [Fact]
    public void Omits_page_headings_when_the_option_is_cleared()
    {
        using var stream = ExportPages(
            new WordExportOptions(PageBreakBetweenPages: false, PageHeadingPerPage: false),
            (12, "Body text"));

        BodyOf(stream).InnerText.Should().NotContain("Page 12");
    }

    [Fact]
    public void Sets_document_properties()
    {
        var stream = new MemoryStream();
        WordExporter.Write(
            stream,
            [new ExportPage(1, MarkdownParser.Parse("x"))],
            new WordExportOptions(SourceFileName: "report.pdf"));
        stream.Position = 0;

        using var doc = WordprocessingDocument.Open(stream, false);

        doc.PackageProperties.Title.Should().Be("report.pdf");
        doc.PackageProperties.Creator.Should().Be("Lumen");
        doc.PackageProperties.Created.Should().NotBeNull();
    }

    [Fact]
    public void Adds_a_header_carrying_the_source_filename()
    {
        var stream = new MemoryStream();
        WordExporter.Write(
            stream,
            [new ExportPage(1, MarkdownParser.Parse("x"))],
            new WordExportOptions(SourceFileName: "quarterly.pdf"));
        stream.Position = 0;

        using var doc = WordprocessingDocument.Open(stream, false);

        doc.MainDocumentPart!.HeaderParts.Should().NotBeEmpty();
        string.Concat(doc.MainDocumentPart.HeaderParts.Select(h => h.Header!.InnerText))
            .Should().Contain("quarterly.pdf");
    }

    [Fact]
    public void Adds_a_footer_with_a_page_number_field()
    {
        var stream = new MemoryStream();
        WordExporter.Write(stream, [new ExportPage(1, MarkdownParser.Parse("x"))], new WordExportOptions());
        stream.Position = 0;

        using var doc = WordprocessingDocument.Open(stream, false);

        doc.MainDocumentPart!.FooterParts.Should().NotBeEmpty();
        doc.MainDocumentPart.FooterParts
            .SelectMany(f => f.Footer!.Descendants<FieldCode>())
            .Select(f => f.Text)
            .Should().Contain(t => t.Contains("PAGE"));
    }

    [Fact]
    public void Empty_page_produces_a_valid_document()
    {
        var act = () =>
        {
            using var stream = Export("");
            using var doc = WordprocessingDocument.Open(stream, false);
            return doc.MainDocumentPart!.Document!.Body;
        };

        act.Should().NotThrow("a blank page is a normal outcome and must not break the export");
    }

    [Fact]
    public void Preserves_content_across_multiple_pages()
    {
        using var stream = ExportPages(
            new WordExportOptions(PageBreakBetweenPages: true, PageHeadingPerPage: false),
            (1, "Alpha content"), (2, "Beta content"));

        var text = BodyOf(stream).InnerText;

        text.Should().Contain("Alpha content").And.Contain("Beta content");
    }

    [Fact]
    public void Rejects_a_null_stream()
    {
        var act = () => WordExporter.Write(null!, [], new WordExportOptions());

        act.Should().Throw<ArgumentNullException>();
    }
}

public class TextExportersTests
{
    private static readonly List<ExportPage> Pages =
    [
        new(1, MarkdownParser.Parse("# One\n\nAlpha")),
        new(7, MarkdownParser.Parse("## Two\n\nBeta"))
    ];

    [Fact]
    public void Markdown_export_preserves_markup()
    {
        var md = TextExporters.ToMarkdown(Pages, includePageHeadings: false);

        md.Should().Contain("# One").And.Contain("Alpha").And.Contain("Beta");
    }

    [Fact]
    public void Markdown_export_can_include_page_headings()
    {
        TextExporters.ToMarkdown(Pages, includePageHeadings: true)
            .Should().Contain("Page 7");
    }

    [Fact]
    public void Plain_text_export_strips_markup()
    {
        var text = TextExporters.ToPlainText(Pages, includePageHeadings: false);

        text.Should().Contain("One").And.Contain("Alpha");
        text.Should().NotContain("#");
    }

    [Fact]
    public void Empty_page_list_yields_empty_output()
    {
        TextExporters.ToMarkdown([], includePageHeadings: true).Should().BeEmpty();
        TextExporters.ToPlainText([], includePageHeadings: true).Should().BeEmpty();
    }
}
