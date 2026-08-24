using FluentAssertions;
using Lumen.Core.Markdown;
using Xunit;

namespace Lumen.Core.Tests.Markdown;

public class TextDirectionDetectorTests
{
    [Fact]
    public void Urdu_text_is_right_to_left() =>
        TextDirectionDetector.IsPredominantlyRightToLeft("یہ ایک اردو جملہ ہے۔").Should().BeTrue();

    [Fact]
    public void Arabic_text_is_right_to_left() =>
        TextDirectionDetector.IsPredominantlyRightToLeft("هذه جملة عربية.").Should().BeTrue();

    [Fact]
    public void Hebrew_text_is_right_to_left() =>
        TextDirectionDetector.IsPredominantlyRightToLeft("זהו משפט בעברית.").Should().BeTrue();

    [Fact]
    public void English_text_is_left_to_right() =>
        TextDirectionDetector.IsPredominantlyRightToLeft("This is an English sentence.").Should().BeFalse();

    [Fact]
    public void Null_or_empty_text_is_left_to_right()
    {
        TextDirectionDetector.IsPredominantlyRightToLeft(null).Should().BeFalse();
        TextDirectionDetector.IsPredominantlyRightToLeft(string.Empty).Should().BeFalse();
    }

    [Fact]
    public void Digits_and_punctuation_alone_are_left_to_right() =>
        TextDirectionDetector.IsPredominantlyRightToLeft("0539/01/M/J/25 - # 2.").Should().BeFalse();

    [Fact]
    public void Urdu_paragraph_with_an_embedded_page_number_is_still_right_to_left() =>
        TextDirectionDetector.IsPredominantlyRightToLeft("مشق نمبر: 1 اردو میں لکھا ہوا صفحہ۔ © UCLES 2025")
            .Should().BeTrue();

    [Fact]
    public void A_short_Latin_heading_inside_a_mostly_Urdu_document_is_still_right_to_left() =>
        TextDirectionDetector.IsPredominantlyRightToLeft("# Section Title\n\nیہ ایک طویل اردو پیراگراف ہے جو صفحے کے بیشتر حصے پر مشتمل ہے۔")
            .Should().BeTrue();
}
