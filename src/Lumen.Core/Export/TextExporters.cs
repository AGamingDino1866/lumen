using System.Text;
using Lumen.Core.Markdown;

namespace Lumen.Core.Export;

/// <summary>
/// Plain-text and markdown exports, offered from the same save dialog as the Word export.
/// </summary>
public static class TextExporters
{
    /// <summary>
    /// Joins pages preserving markdown, optionally with a "Page N" heading before each.
    /// </summary>
    public static string ToMarkdown(IReadOnlyList<ExportPage> pages, bool includePageHeadings)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var builder = new StringBuilder();

        foreach (var page in pages)
        {
            var content = Render(page.Document);

            if (!includePageHeadings && content.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            if (includePageHeadings)
            {
                builder.Append("## Page ").Append(page.PageNumber).Append("\n\n");
            }

            builder.Append(content);
        }

        return builder.ToString().Trim();
    }

    /// <summary>Joins pages with all markdown syntax removed.</summary>
    public static string ToPlainText(IReadOnlyList<ExportPage> pages, bool includePageHeadings)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var builder = new StringBuilder();

        foreach (var page in pages)
        {
            var content = MarkdownStripper.ToPlainText(page.Document);

            if (!includePageHeadings && content.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            if (includePageHeadings)
            {
                builder.Append("Page ").Append(page.PageNumber).Append("\n\n");
            }

            builder.Append(content);
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Renders the parsed model back to markdown. Going through the model rather than keeping the
    /// original string means the markdown export and the Word export always agree about
    /// structure: anything the parser dropped is absent from both, never from just one.
    /// </summary>
    private static string Render(MarkdownDocument document)
    {
        var builder = new StringBuilder();

        foreach (var block in document.Blocks)
        {
            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            switch (block)
            {
                case MdHeading heading:
                    builder.Append(new string('#', Math.Clamp(heading.Level, 1, 6)))
                           .Append(' ')
                           .Append(RenderInlines(heading.Inlines));
                    break;

                case MdParagraph paragraph:
                    builder.Append(RenderInlines(paragraph.Inlines));
                    break;

                case MdTable table:
                    RenderTable(builder, table);
                    break;

                case MdList list:
                    for (var i = 0; i < list.Items.Count; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append('\n');
                        }

                        builder.Append(list.Ordered ? $"{i + 1}. " : "- ")
                               .Append(RenderInlines(list.Items[i].Inlines));
                    }
                    break;

                case MdCode code:
                    builder.Append("```\n").Append(code.Code).Append("\n```");
                    break;
            }
        }

        return builder.ToString();
    }

    private static void RenderTable(StringBuilder builder, MdTable table)
    {
        var columns = Math.Max(table.Header.Count, table.Rows.Count > 0 ? table.Rows.Max(r => r.Count) : 0);

        if (columns == 0)
        {
            return;
        }

        builder.Append("| ")
               .Append(string.Join(" | ", Enumerable.Range(0, columns)
                   .Select(i => i < table.Header.Count ? RenderInlines(table.Header[i].Inlines) : string.Empty)))
               .Append(" |\n");

        builder.Append('|')
               .Append(string.Concat(Enumerable.Repeat(" --- |", columns)))
               .Append('\n');

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];

            builder.Append("| ")
                   .Append(string.Join(" | ", Enumerable.Range(0, columns)
                       .Select(i => i < row.Count ? RenderInlines(row[i].Inlines) : string.Empty)))
                   .Append(" |");

            if (r < table.Rows.Count - 1)
            {
                builder.Append('\n');
            }
        }
    }

    private static string RenderInlines(IReadOnlyList<MdInline> inlines)
    {
        var builder = new StringBuilder();

        foreach (var inline in inlines)
        {
            var marker = (inline.Bold, inline.Italic) switch
            {
                (true, true) => "***",
                (true, false) => "**",
                (false, true) => "*",
                _ => string.Empty
            };

            builder.Append(marker).Append(inline.Text).Append(marker);
        }

        return builder.ToString();
    }
}
