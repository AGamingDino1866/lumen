using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Lumen.Core.Markdown;

/// <summary>
/// Parses the markdown Gemini returns into <see cref="MarkdownDocument"/>.
/// </summary>
/// <remarks>
/// Markdig does the CommonMark work; this class flattens its AST into a small model that the
/// two renderers (WPF results panel and OpenXML exporter) both consume. Parsing once into a
/// shared model means the panel and the exported document can never disagree about structure.
/// <para>
/// Hand-rolling this was rejected: pipe tables and nested emphasis are exactly where a
/// bespoke CommonMark parser goes wrong.
/// </para>
/// </remarks>
public static class MarkdownParser
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()   // required for the markdown tables the extraction prompt requests
        .UseEmphasisExtras()
        .Build();

    public static MarkdownDocument Parse(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return MarkdownDocument.Empty;
        }

        var document = Markdig.Markdown.Parse(markdown, Pipeline);
        var blocks = new List<MdBlock>();

        foreach (var block in document)
        {
            AppendBlock(block, blocks);
        }

        return new MarkdownDocument(blocks);
    }

    private static void AppendBlock(Block block, List<MdBlock> output)
    {
        switch (block)
        {
            case HeadingBlock heading:
                output.Add(new MdHeading(
                    Math.Clamp(heading.Level, 1, 6),
                    ReadInlines(heading.Inline)));
                break;

            case ParagraphBlock paragraph:
                var inlines = ReadInlines(paragraph.Inline);
                if (inlines.Count > 0)
                {
                    output.Add(new MdParagraph(inlines));
                }
                break;

            case Table table:
                output.Add(ReadTable(table));
                break;

            case ListBlock list:
                output.Add(ReadList(list));
                break;

            case CodeBlock code:
                output.Add(new MdCode(ReadCode(code)));
                break;

            case QuoteBlock quote:
                // Blockquotes are flattened into their constituent blocks. The extraction prompt
                // does not ask for them, so preserving quote nesting would add a Word style
                // for a construct that should not appear.
                foreach (var child in quote)
                {
                    AppendBlock(child, output);
                }
                break;

            case ContainerBlock container:
                foreach (var child in container)
                {
                    AppendBlock(child, output);
                }
                break;
        }
    }

    private static MdTable ReadTable(Table table)
    {
        var header = new List<MdCell>();
        var rows = new List<IReadOnlyList<MdCell>>();

        foreach (var child in table)
        {
            if (child is not TableRow row)
            {
                continue;
            }

            var cells = new List<MdCell>();
            foreach (var cellObject in row)
            {
                if (cellObject is not TableCell cell)
                {
                    continue;
                }

                var cellInlines = new List<MdInline>();
                foreach (var cellBlock in cell)
                {
                    if (cellBlock is ParagraphBlock p)
                    {
                        cellInlines.AddRange(ReadInlines(p.Inline));
                    }
                }

                cells.Add(new MdCell(cellInlines));
            }

            if (row.IsHeader && header.Count == 0)
            {
                header.AddRange(cells);
            }
            else
            {
                rows.Add(cells);
            }
        }

        return new MdTable(header, rows);
    }

    private static MdList ReadList(ListBlock list)
    {
        var items = new List<MdCell>();

        foreach (var itemObject in list)
        {
            if (itemObject is not ListItemBlock item)
            {
                continue;
            }

            var itemInlines = new List<MdInline>();
            foreach (var itemBlock in item)
            {
                if (itemBlock is ParagraphBlock p)
                {
                    itemInlines.AddRange(ReadInlines(p.Inline));
                }
            }

            items.Add(new MdCell(itemInlines));
        }

        return new MdList(list.IsOrdered, items);
    }

    private static string ReadCode(CodeBlock code)
    {
        if (code.Lines.Lines is null)
        {
            return string.Empty;
        }

        var lines = new List<string>();
        for (var i = 0; i < code.Lines.Count; i++)
        {
            lines.Add(code.Lines.Lines[i].Slice.ToString());
        }

        return string.Join("\n", lines);
    }

    private static IReadOnlyList<MdInline> ReadInlines(ContainerInline? container)
    {
        var result = new List<MdInline>();

        if (container is not null)
        {
            Walk(container, bold: false, italic: false, result);
        }

        return Merge(result);
    }

    private static void Walk(ContainerInline container, bool bold, bool italic, List<MdInline> output)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    var text = literal.Content.ToString();
                    if (text.Length > 0)
                    {
                        output.Add(new MdInline(text, bold, italic));
                    }
                    break;

                case EmphasisInline emphasis:
                    // Markdig reports run length: 1 is italic, 2 is bold, 3 is both. Nested
                    // emphasis therefore accumulates rather than replaces.
                    var nowBold = bold || emphasis.DelimiterCount >= 2;
                    var nowItalic = italic || emphasis.DelimiterCount == 1 || emphasis.DelimiterCount >= 3;
                    Walk(emphasis, nowBold, nowItalic, output);
                    break;

                case LineBreakInline:
                    output.Add(new MdInline(" ", bold, italic));
                    break;

                case CodeInline code:
                    output.Add(new MdInline(code.Content, bold, italic));
                    break;

                case LinkInline link:
                    // Transcription should not produce links, but if one appears its visible
                    // text is what belongs in the document, not the URL.
                    Walk(link, bold, italic, output);
                    break;

                case ContainerInline nested:
                    Walk(nested, bold, italic, output);
                    break;
            }
        }
    }

    /// <summary>
    /// Coalesces adjacent runs that share formatting, so "plain" text does not arrive as a
    /// dozen one-character runs in the exported document.
    /// </summary>
    private static IReadOnlyList<MdInline> Merge(List<MdInline> inlines)
    {
        if (inlines.Count <= 1)
        {
            return inlines;
        }

        var merged = new List<MdInline>(inlines.Count) { inlines[0] };

        for (var i = 1; i < inlines.Count; i++)
        {
            var previous = merged[^1];
            var current = inlines[i];

            if (previous.Bold == current.Bold && previous.Italic == current.Italic)
            {
                merged[^1] = previous with { Text = previous.Text + current.Text };
            }
            else
            {
                merged.Add(current);
            }
        }

        return merged;
    }
}
