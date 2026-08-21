using FluentAssertions;
using Lumen.Core.Gemini;
using Xunit;

namespace Lumen.Core.Tests.Gemini;

public class ExtractionPromptLoaderTests
{
    [Fact]
    public void Strips_every_commentary_line()
    {
        ExtractionPromptLoader.Load().Should().NotContain("#!",
            "commentary documents the clauses for maintainers and must never be sent to the model");
    }

    [Fact]
    public void Retains_the_substantive_clauses()
    {
        var prompt = ExtractionPromptLoader.Load();

        prompt.Should().Contain("verbatim");
        prompt.Should().Contain("markdown tables");
        prompt.Should().Contain("heading hierarchy");
        prompt.Should().Contain("Never summarise");
        prompt.Should().Contain("return nothing at all");
        prompt.Should().Contain("code fence");
    }

    [Fact]
    public void Instructs_the_model_to_describe_rather_than_skip_figures()
    {
        ExtractionPromptLoader.Load().Should().Contain("[Figure:");
    }

    [Fact]
    public void Has_no_blank_run_longer_than_one_line()
    {
        ExtractionPromptLoader.Load().Should().NotContain("\n\n\n",
            "stripping commentary must not leave large gaps that waste tokens");
    }

    [Fact]
    public void Is_not_empty_and_does_not_start_or_end_with_whitespace()
    {
        var prompt = ExtractionPromptLoader.Load();

        prompt.Should().NotBeNullOrWhiteSpace();
        prompt.Should().Be(prompt.Trim());
    }

    [Fact]
    public void Is_cached_so_repeated_calls_return_the_same_instance()
    {
        ReferenceEquals(ExtractionPromptLoader.Load(), ExtractionPromptLoader.Load())
            .Should().BeTrue("the prompt is read once per process, not per page");
    }
}
