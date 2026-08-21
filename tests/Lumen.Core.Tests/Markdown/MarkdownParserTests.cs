using FluentAssertions;
using Lumen.Core.Markdown;
using Xunit;

namespace Lumen.Core.Tests.Markdown;

public class MarkdownParserTests
{
    private static MarkdownDocument Parse(string markdown) => MarkdownParser.Parse(markdown);

    [Theory]
    [InlineData("# One", 1)]
    [InlineData("## Two", 2)]
    [InlineData("### Three", 3)]
    public void Parses_headings_at_each_level(string markdown, int level)
    {
        var heading = Parse(markdown).Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<MdHeading>().Subject;

        heading.Level.Should().Be(level);
        heading.Text.Should().Be(level switch { 1 => "One", 2 => "Two", _ => "Three" });
    }

    [Fact]
    public void Parses_a_paragraph()
    {
        Parse("Just some prose.").Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<MdParagraph>()
            .Which.Text.Should().Be("Just some prose.");
    }

    [Fact]
    public void Preserves_bold_runs()
    {
        var para = Parse("plain **bold** plain").Blocks.OfType<MdParagraph>().Single();

        para.Inlines.Should().Contain(i => i.Bold && i.Text == "bold");
        para.Inlines.Should().Contain(i => !i.Bold && i.Text.Contains("plain"));
    }

    [Fact]
    public void Preserves_italic_runs()
    {
        Parse("an *italic* word").Blocks.OfType<MdParagraph>().Single()
            .Inlines.Should().Contain(i => i.Italic && i.Text == "italic");
    }

    [Fact]
    public void Preserves_bold_italic_runs()
    {
        Parse("***both***").Blocks.OfType<MdParagraph>().Single()
            .Inlines.Should().Contain(i => i.Bold && i.Italic);
    }

    [Fact]
    public void Parses_a_pipe_table_with_a_header_row()
    {
        const string md = """
            | Region | Revenue |
            | ------ | ------- |
            | North  | 1200    |
            | South  | 980     |
            """;

        var table = Parse(md).Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<MdTable>().Subject;

        table.Header.Should().HaveCount(2);
        table.Header[0].Text.Should().Be("Region");
        table.Header[1].Text.Should().Be("Revenue");
        table.Rows.Should().HaveCount(2);
        table.Rows[0][0].Text.Should().Be("North");
        table.Rows[1][1].Text.Should().Be("980");
    }

    [Fact]
    public void Table_preserves_empty_cells()
    {
        const string md = """
            | A | B |
            | - | - |
            |   | x |
            """;

        Parse(md).Blocks.OfType<MdTable>().Single().Rows[0].Should().HaveCount(2);
    }

    [Fact]
    public void Parses_a_bulleted_list()
    {
        var list = Parse("- alpha\n- beta").Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<MdList>().Subject;

        list.Ordered.Should().BeFalse();
        list.Items.Should().HaveCount(2);
        list.Items[0].Text.Should().Be("alpha");
    }

    [Fact]
    public void Parses_a_numbered_list()
    {
        var list = Parse("1. first\n2. second").Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<MdList>().Subject;

        list.Ordered.Should().BeTrue();
        list.Items.Should().HaveCount(2);
        list.Items[1].Text.Should().Be("second");
    }

    [Fact]
    public void Parses_a_figure_note_as_a_paragraph()
    {
        // The extraction prompt asks the model to emit bracketed figure notes; they must survive.
        Parse("[Figure: bar chart of quarterly revenue]").Blocks.OfType<MdParagraph>().Single()
            .Text.Should().Contain("Figure:");
    }

    [Fact]
    public void Parses_a_mixed_document_in_order()
    {
        const string md = """
            # Report

            Opening prose.

            ## Detail

            - one
            - two
            """;

        var kinds = Parse(md).Blocks.Select(b => b.GetType().Name).ToList();

        kinds.Should().Equal("MdHeading", "MdParagraph", "MdHeading", "MdList");
    }

    [Fact]
    public void Empty_input_yields_no_blocks()
    {
        Parse("").Blocks.Should().BeEmpty();
    }

    [Fact]
    public void Null_input_does_not_throw()
    {
        var act = () => MarkdownParser.Parse(null!);

        act.Should().NotThrow();
        act().Blocks.Should().BeEmpty();
    }

    [Fact]
    public void Plain_text_without_markdown_survives_intact()
    {
        Parse("No markup at all here.").Blocks.OfType<MdParagraph>().Single()
            .Text.Should().Be("No markup at all here.");
    }
}

public class MarkdownStripperTests
{
    [Fact]
    public void Removes_heading_markers()
    {
        MarkdownStripper.ToPlainText("# Title").Trim().Should().Be("Title");
    }

    [Fact]
    public void Removes_emphasis_markers_but_keeps_the_words()
    {
        var text = MarkdownStripper.ToPlainText("a **bold** and *italic* line");

        text.Should().Contain("bold").And.Contain("italic");
        text.Should().NotContain("*");
    }

    [Fact]
    public void Renders_a_table_as_tab_separated_rows()
    {
        const string md = """
            | A | B |
            | - | - |
            | 1 | 2 |
            """;

        var text = MarkdownStripper.ToPlainText(md);

        text.Should().Contain("A\tB");
        text.Should().Contain("1\t2");
        text.Should().NotContain("|");
    }

    [Fact]
    public void Removes_list_markers_but_keeps_items()
    {
        var text = MarkdownStripper.ToPlainText("- alpha\n- beta");

        text.Should().Contain("alpha").And.Contain("beta");
        text.Should().NotStartWith("-");
    }

    [Fact]
    public void Loses_no_words_from_a_mixed_document()
    {
        const string md = "# Head\n\nSome **prose** here.\n\n- item";

        var text = MarkdownStripper.ToPlainText(md);

        foreach (var word in new[] { "Head", "Some", "prose", "here", "item" })
        {
            text.Should().Contain(word);
        }
    }

    [Fact]
    public void Empty_input_yields_empty_output()
    {
        MarkdownStripper.ToPlainText("").Should().BeEmpty();
    }
}
