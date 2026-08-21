using System.Text;

namespace Lumen.Core.Markdown;

/// <summary>
/// Converts markdown to plain text for the "copy as plain text" option and the .txt export.
/// </summary>
/// <remarks>
/// Works over the parsed <see cref="MarkdownDocument"/> rather than by running regular
/// expressions across the raw string. Regex stripping mangles tables and any literal asterisk
/// that was part of the transcribed page rather than markup.
/// </remarks>
public static class MarkdownStripper
{
    public static string ToPlainText(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        return ToPlainText(MarkdownParser.Parse(markdown));
    }

    public static string ToPlainText(MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();

        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case MdHeading heading:
                    Append(builder, heading.Text);
                    break;

                case MdParagraph paragraph:
                    Append(builder, paragraph.Text);
                    break;

                case MdTable table:
                    // Tab separation keeps columns aligned when pasted into a spreadsheet,
                    // which is the usual destination for a transcribed table.
                    Append(builder, string.Join("\t", table.Header.Select(c => c.Text)));
                    foreach (var row in table.Rows)
                    {
                        Append(builder, string.Join("\t", row.Select(c => c.Text)), separateBlocks: false);
                    }
                    break;

                case MdList list:
                    foreach (var item in list.Items)
                    {
                        Append(builder, item.Text, separateBlocks: false);
                    }
                    break;

                case MdCode code:
                    Append(builder, code.Code);
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    private static void Append(StringBuilder builder, string text, bool separateBlocks = true)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append(separateBlocks ? "\n\n" : "\n");
        }

        builder.Append(text);
    }
}
