using FluentAssertions;
using Lumen.Core.Selection;
using Xunit;

namespace Lumen.Core.Tests.Selection;

public class PageRangeParserTests
{
    private const int PageCount = 100;

    [Fact]
    public void Parses_mixed_ranges_and_singles()
    {
        PageRangeParser.Parse("1-5, 12, 20-24", PageCount).Pages
            .Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5, 12, 20, 21, 22, 23, 24 });
    }

    [Fact]
    public void Ignores_surrounding_and_internal_whitespace()
    {
        PageRangeParser.Parse("  3 ,  7 - 9  ", PageCount).Pages
            .Should().BeEquivalentTo(new[] { 3, 7, 8, 9 });
    }

    [Fact]
    public void Deduplicates_overlapping_ranges()
    {
        PageRangeParser.Parse("1-5,3-7", PageCount).Pages
            .Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5, 6, 7 });
    }

    [Fact]
    public void Returns_pages_in_ascending_order()
    {
        PageRangeParser.Parse("20-21, 3, 10", PageCount).Pages
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public void Single_page_is_accepted()
    {
        PageRangeParser.Parse("42", PageCount).Pages.Should().BeEquivalentTo(new[] { 42 });
    }

    [Fact]
    public void Range_of_one_is_accepted()
    {
        PageRangeParser.Parse("7-7", PageCount).Pages.Should().BeEquivalentTo(new[] { 7 });
    }

    [Fact]
    public void Trailing_comma_is_tolerated()
    {
        PageRangeParser.Parse("1,2,", PageCount).Pages.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public void Valid_input_reports_no_error()
    {
        PageRangeParser.Parse("1-5", PageCount).Error.Should().BeNull();
    }

    [Fact]
    public void Reversed_range_is_an_error()
    {
        var result = PageRangeParser.Parse("9-3", PageCount);
        result.Error.Should().NotBeNull();
        result.Pages.Should().BeEmpty();
    }

    [Fact]
    public void Range_beyond_the_document_is_an_error()
    {
        PageRangeParser.Parse("1-500", PageCount).Error.Should().NotBeNull();
    }

    [Fact]
    public void Single_page_beyond_the_document_is_an_error()
    {
        PageRangeParser.Parse("500", PageCount).Error.Should().NotBeNull();
    }

    [Fact]
    public void Zero_is_an_error_because_pages_are_one_based()
    {
        PageRangeParser.Parse("0-3", PageCount).Error.Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1--3")]
    [InlineData("1-")]
    [InlineData("-5")]
    [InlineData(",")]
    [InlineData("1-2-3")]
    [InlineData("1,,2")]
    [InlineData("99999999999999999999")]
    public void Malformed_input_reports_an_error_and_never_throws(string input)
    {
        var act = () => PageRangeParser.Parse(input, PageCount);

        act.Should().NotThrow();
        var result = act();
        result.Error.Should().NotBeNull();
        result.Pages.Should().BeEmpty();
    }

    [Fact]
    public void Error_message_is_human_readable_and_names_the_offending_token()
    {
        PageRangeParser.Parse("1-5, banana", PageCount).Error
            .Should().Contain("banana");
    }
}
