namespace Lumen.Core.Markdown;

/// <summary>
/// A run of text with its emphasis. The smallest unit both renderers understand.
/// </summary>
public sealed record MdInline(string Text, bool Bold = false, bool Italic = false);

/// <summary>Base type for everything that can appear at block level.</summary>
public abstract record MdBlock
{
    /// <summary>The block's text with all emphasis flattened. Convenient for assertions and plain-text export.</summary>
    public abstract string Text { get; }
}

/// <summary>A markdown heading. <paramref name="Level"/> maps directly onto Word's Heading 1-3 styles.</summary>
public sealed record MdHeading(int Level, IReadOnlyList<MdInline> Inlines) : MdBlock
{
    public override string Text => string.Concat(Inlines.Select(i => i.Text));
}

public sealed record MdParagraph(IReadOnlyList<MdInline> Inlines) : MdBlock
{
    public override string Text => string.Concat(Inlines.Select(i => i.Text));
}

/// <summary>A single table cell.</summary>
public sealed record MdCell(IReadOnlyList<MdInline> Inlines)
{
    public string Text => string.Concat(Inlines.Select(i => i.Text));
}

/// <summary>
/// A table with a header row. Produced from the markdown tables the extraction prompt asks for,
/// and converted into a real Word table on export.
/// </summary>
public sealed record MdTable(IReadOnlyList<MdCell> Header, IReadOnlyList<IReadOnlyList<MdCell>> Rows) : MdBlock
{
    public override string Text =>
        string.Join(
            "\n",
            new[] { string.Join("\t", Header.Select(c => c.Text)) }
                .Concat(Rows.Select(r => string.Join("\t", r.Select(c => c.Text)))));
}

/// <summary>A bulleted or numbered list. Becomes real Word list numbering on export.</summary>
public sealed record MdList(bool Ordered, IReadOnlyList<MdCell> Items) : MdBlock
{
    public override string Text => string.Join("\n", Items.Select(i => i.Text));
}

/// <summary>A fenced or indented code block, preserved verbatim.</summary>
public sealed record MdCode(string Code) : MdBlock
{
    public override string Text => Code;
}

/// <summary>A parsed markdown document as an ordered list of blocks.</summary>
public sealed record MarkdownDocument(IReadOnlyList<MdBlock> Blocks)
{
    public static MarkdownDocument Empty { get; } = new([]);

    /// <summary>Every block's text, joined by blank lines.</summary>
    public string PlainText =>
        string.Join("\n\n", Blocks.Select(b => b.Text).Where(t => t.Length > 0));
}
