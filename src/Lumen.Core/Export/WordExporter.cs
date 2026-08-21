using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Lumen.Core.Markdown;

namespace Lumen.Core.Export;

/// <summary>One transcribed source page destined for the exported document.</summary>
public sealed record ExportPage(int PageNumber, MarkdownDocument Document);

/// <summary>Options for a Word export.</summary>
/// <param name="PageBreakBetweenPages">
/// Insert a page break between source pages. Defaults to true, matching the user's choice.
/// </param>
/// <param name="PageHeadingPerPage">Emit a "Page N" heading before each source page.</param>
/// <param name="SourceFileName">Used for the document title and the page header.</param>
public sealed record WordExportOptions(
    bool PageBreakBetweenPages = true,
    bool PageHeadingPerPage = true,
    string SourceFileName = "");

/// <summary>
/// Writes a real <c>.docx</c> using the Open XML SDK.
/// </summary>
/// <remarks>
/// Word Interop was rejected outright: it requires Microsoft Word installed on the target
/// machine, which would defeat the point of a self-contained installer.
/// <para>
/// The styles and numbering parts are created explicitly. Word silently ignores a
/// <c>ParagraphStyleId</c> the document does not define, so writing "Heading1" without a
/// matching style definition produces a document whose navigation pane is empty — the exact
/// failure this exporter exists to avoid.
/// </para>
/// </remarks>
public static class WordExporter
{
    private const string BodyFont = "Calibri";
    private const int ListNumberId = 1;

    public static void Write(Stream target, IReadOnlyList<ExportPage> pages, WordExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(options);

        using var package = WordprocessingDocument.Create(target, WordprocessingDocumentType.Document);

        var mainPart = package.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body!;

        AddStyles(mainPart);
        AddNumbering(mainPart);

        var headerId = AddHeader(mainPart, options.SourceFileName);
        var footerId = AddFooter(mainPart);

        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];

            if (options.PageBreakBetweenPages && i > 0)
            {
                body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
            }

            if (options.PageHeadingPerPage)
            {
                body.AppendChild(StyledParagraph($"Page {page.PageNumber}", "Heading2"));
            }

            foreach (var block in page.Document.Blocks)
            {
                AppendBlock(body, block);
            }
        }

        body.AppendChild(SectionProperties(headerId, footerId));

        SetPackageProperties(package, options);

        mainPart.Document.Save();
    }

    private static void AppendBlock(Body body, MdBlock block)
    {
        switch (block)
        {
            case MdHeading heading:
                var level = Math.Clamp(heading.Level, 1, 3);
                var paragraph = new Paragraph(new ParagraphProperties(
                    new ParagraphStyleId { Val = $"Heading{level}" }));
                AppendRuns(paragraph, heading.Inlines);
                body.AppendChild(paragraph);
                break;

            case MdParagraph para:
                var p = new Paragraph();
                AppendRuns(p, para.Inlines);
                body.AppendChild(p);
                break;

            case MdTable table:
                body.AppendChild(BuildTable(table));
                break;

            case MdList list:
                foreach (var item in list.Items)
                {
                    body.AppendChild(BuildListItem(item, list.Ordered));
                }
                break;

            case MdCode code:
                foreach (var line in code.Code.Split('\n'))
                {
                    body.AppendChild(new Paragraph(
                        new ParagraphProperties(new ParagraphStyleId { Val = "LumenCode" }),
                        new Run(new Text(line) { Space = SpaceProcessingModeValues.Preserve })));
                }
                break;
        }
    }

    private static void AppendRuns(Paragraph paragraph, IReadOnlyList<MdInline> inlines)
    {
        foreach (var inline in inlines)
        {
            paragraph.AppendChild(BuildRun(inline));
        }
    }

    private static Run BuildRun(MdInline inline)
    {
        var run = new Run();

        if (inline.Bold || inline.Italic)
        {
            var properties = new RunProperties();

            if (inline.Bold)
            {
                properties.AppendChild(new Bold());
            }

            if (inline.Italic)
            {
                properties.AppendChild(new Italic());
            }

            run.AppendChild(properties);
        }

        // Space preservation matters: markdown runs frequently begin or end with a space
        // ("plain **bold** plain"), and Word trims them otherwise.
        run.AppendChild(new Text(inline.Text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static Table BuildTable(MdTable source)
    {
        var table = new Table();

        table.AppendChild(new TableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }),
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct }));

        if (source.Header.Count > 0)
        {
            var headerRow = new TableRow();

            // TableHeader makes the row repeat across page breaks, which matters for a
            // transcribed table long enough to span pages.
            headerRow.AppendChild(new TableRowProperties(new TableHeader()));

            foreach (var cell in source.Header)
            {
                headerRow.AppendChild(BuildCell(cell, bold: true));
            }

            table.AppendChild(headerRow);
        }

        foreach (var row in source.Rows)
        {
            var tableRow = new TableRow();

            foreach (var cell in row)
            {
                tableRow.AppendChild(BuildCell(cell, bold: false));
            }

            table.AppendChild(tableRow);
        }

        return table;
    }

    private static TableCell BuildCell(MdCell cell, bool bold)
    {
        var paragraph = new Paragraph();

        if (cell.Inlines.Count == 0)
        {
            // A cell must contain at least one paragraph or Word reports the file as corrupt.
            paragraph.AppendChild(new Run(new Text(string.Empty)));
        }
        else
        {
            foreach (var inline in cell.Inlines)
            {
                paragraph.AppendChild(BuildRun(bold ? inline with { Bold = true } : inline));
            }
        }

        return new TableCell(
            new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Auto }),
            paragraph);
    }

    private static Paragraph BuildListItem(MdCell item, bool ordered)
    {
        var paragraph = new Paragraph(new ParagraphProperties(
            new ParagraphStyleId { Val = "ListParagraph" },
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                // Two abstract numbering definitions are registered; bulleted uses the first.
                new NumberingId { Val = ordered ? ListNumberId + 1 : ListNumberId })));

        AppendRuns(paragraph, item.Inlines);
        return paragraph;
    }

    private static Paragraph StyledParagraph(string text, string styleId) =>
        new(
            new ParagraphProperties(new ParagraphStyleId { Val = styleId }),
            new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static void AddStyles(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles();

        styles.AppendChild(new DocDefaults(
            new RunPropertiesDefault(new RunPropertiesBaseStyle(
                new RunFonts { Ascii = BodyFont, HighAnsi = BodyFont },
                new FontSize { Val = "22" }))));

        // Heading sizes are in half-points: 32 = 16pt, 26 = 13pt, 24 = 12pt.
        styles.AppendChild(HeadingStyle("Heading1", "heading 1", "32", 1));
        styles.AppendChild(HeadingStyle("Heading2", "heading 2", "26", 2));
        styles.AppendChild(HeadingStyle("Heading3", "heading 3", "24", 3));

        styles.AppendChild(new Style(
            new StyleName { Val = "List Paragraph" },
            new PrimaryStyle())
        {
            Type = StyleValues.Paragraph,
            StyleId = "ListParagraph"
        });

        styles.AppendChild(new Style(
            new StyleName { Val = "Lumen Code" },
            new StyleRunProperties(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" }))
        {
            Type = StyleValues.Paragraph,
            StyleId = "LumenCode"
        });

        stylesPart.Styles = styles;
        stylesPart.Styles.Save();
    }

    private static Style HeadingStyle(string styleId, string name, string halfPointSize, int outlineLevel) =>
        new(
            new StyleName { Val = name },
            new BasedOn { Val = "Normal" },
            new NextParagraphStyle { Val = "Normal" },
            new PrimaryStyle(),
            new StyleParagraphProperties(
                new KeepNext(),
                new SpacingBetweenLines { Before = "240", After = "120" },
                // OutlineLevel is what places the heading in Word's navigation pane.
                new OutlineLevel { Val = outlineLevel - 1 }),
            new StyleRunProperties(
                new Bold(),
                new FontSize { Val = halfPointSize }))
        {
            Type = StyleValues.Paragraph,
            StyleId = styleId
        };

    private static void AddNumbering(MainDocumentPart mainPart)
    {
        var numberingPart = mainPart.AddNewPart<NumberingDefinitionsPart>();

        var bulletAbstract = new AbstractNum(
            new Level(
                new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = "•" },
                new StartNumberingValue { Val = 1 },
                new PreviousParagraphProperties(
                    new Indentation { Left = "720", Hanging = "360" }),
                new NumberingSymbolRunProperties(
                    new RunFonts { Ascii = "Symbol", HighAnsi = "Symbol" }))
            { LevelIndex = 0 })
        { AbstractNumberId = 0 };

        var orderedAbstract = new AbstractNum(
            new Level(
                new NumberingFormat { Val = NumberFormatValues.Decimal },
                new LevelText { Val = "%1." },
                new StartNumberingValue { Val = 1 },
                new PreviousParagraphProperties(
                    new Indentation { Left = "720", Hanging = "360" }))
            { LevelIndex = 0 })
        { AbstractNumberId = 1 };

        var numbering = new Numbering(
            bulletAbstract,
            orderedAbstract,
            new NumberingInstance(new AbstractNumId { Val = 0 }) { NumberID = ListNumberId },
            new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = ListNumberId + 1 });

        numberingPart.Numbering = numbering;
        numberingPart.Numbering.Save();
    }

    private static string AddHeader(MainDocumentPart mainPart, string sourceFileName)
    {
        var headerPart = mainPart.AddNewPart<HeaderPart>();

        var text = string.IsNullOrWhiteSpace(sourceFileName)
            ? "Extracted with Lumen"
            : $"{sourceFileName} — extracted with Lumen";

        headerPart.Header = new Header(
            new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Right }),
                new Run(
                    new RunProperties(new FontSize { Val = "18" }, new Color { Val = "767676" }),
                    new Text(text) { Space = SpaceProcessingModeValues.Preserve })));

        headerPart.Header.Save();
        return mainPart.GetIdOfPart(headerPart);
    }

    private static string AddFooter(MainDocumentPart mainPart)
    {
        var footerPart = mainPart.AddNewPart<FooterPart>();

        // A PAGE field, so Word renumbers automatically rather than baking in a static number.
        footerPart.Footer = new Footer(
            new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                new Run(
                    new RunProperties(new FontSize { Val = "18" }, new Color { Val = "767676" }),
                    new FieldChar { FieldCharType = FieldCharValues.Begin }),
                new Run(new FieldCode(" PAGE ") { Space = SpaceProcessingModeValues.Preserve }),
                new Run(new FieldChar { FieldCharType = FieldCharValues.End })));

        footerPart.Footer.Save();
        return mainPart.GetIdOfPart(footerPart);
    }

    private static SectionProperties SectionProperties(string headerId, string footerId) =>
        new(
            new HeaderReference { Type = HeaderFooterValues.Default, Id = headerId },
            new FooterReference { Type = HeaderFooterValues.Default, Id = footerId },
            new PageSize { Width = 11906, Height = 16838 },
            new PageMargin { Top = 1134, Bottom = 1134, Left = 1134, Right = 1134, Header = 709, Footer = 709 });

    private static void SetPackageProperties(WordprocessingDocument package, WordExportOptions options)
    {
        var now = DateTime.UtcNow;

        package.PackageProperties.Title = string.IsNullOrWhiteSpace(options.SourceFileName)
            ? "Extracted text"
            : options.SourceFileName;
        package.PackageProperties.Creator = "Lumen";
        package.PackageProperties.Created = now;
        package.PackageProperties.Modified = now;
    }
}
