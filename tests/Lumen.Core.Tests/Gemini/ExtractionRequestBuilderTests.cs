using System.Text.Json;
using FluentAssertions;
using Lumen.Core.Gemini;
using Xunit;

namespace Lumen.Core.Tests.Gemini;

public class ExtractionRequestBuilderTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement Parts(string json) =>
        Root(json).GetProperty("contents")[0].GetProperty("parts");

    [Fact]
    public void Produces_valid_json()
    {
        var act = () => JsonDocument.Parse(ExtractionRequestBuilder.BuildJson(Png, "PROMPT"));

        act.Should().NotThrow();
    }

    [Fact]
    public void Places_the_text_part_before_the_image_part()
    {
        var parts = Parts(ExtractionRequestBuilder.BuildJson(Png, "PROMPT"));

        parts.GetArrayLength().Should().Be(2);
        parts[0].TryGetProperty("text", out _).Should().BeTrue("the instruction must precede the image");
        parts[1].TryGetProperty("inline_data", out _).Should().BeTrue();
    }

    [Fact]
    public void Carries_the_prompt_text_verbatim()
    {
        Parts(ExtractionRequestBuilder.BuildJson(Png, "TRANSCRIBE THIS"))[0]
            .GetProperty("text").GetString().Should().Be("TRANSCRIBE THIS");
    }

    [Fact]
    public void Base64_encodes_the_image_exactly()
    {
        Parts(ExtractionRequestBuilder.BuildJson(Png, "P"))[1]
            .GetProperty("inline_data").GetProperty("data").GetString()
            .Should().Be(Convert.ToBase64String(Png));
    }

    [Fact]
    public void Declares_the_png_mime_type()
    {
        Parts(ExtractionRequestBuilder.BuildJson(Png, "P"))[1]
            .GetProperty("inline_data").GetProperty("mime_type").GetString()
            .Should().Be("image/png");
    }

    [Fact]
    public void Sets_temperature_to_zero()
    {
        Root(ExtractionRequestBuilder.BuildJson(Png, "P"))
            .GetProperty("generationConfig").GetProperty("temperature").GetDouble()
            .Should().Be(0, "transcription must be deterministic, not creative");
    }

    [Fact]
    public void Escapes_prompt_text_containing_json_metacharacters()
    {
        const string awkward = "quote \" backslash \\ newline \n";

        Parts(ExtractionRequestBuilder.BuildJson(Png, awkward))[0]
            .GetProperty("text").GetString().Should().Be(awkward);
    }

    [Fact]
    public void Handles_an_empty_image_without_throwing()
    {
        var act = () => ExtractionRequestBuilder.BuildJson([], "P");

        act.Should().NotThrow();
    }

    [Fact]
    public void Never_embeds_an_api_key()
    {
        // The key belongs in the x-goog-api-key header, never in the request body.
        var json = ExtractionRequestBuilder.BuildJson(Png, "P");

        json.Should().NotContain("api_key");
        json.Should().NotContain("apiKey");
        json.Should().NotContain("AIza");
    }
}
