using System.Text.Json.Serialization;

namespace Lumen.Core.Gemini;

/// <summary>Outcome of transcribing one page.</summary>
/// <param name="Success">False only when the page genuinely failed; a blank page is a success with empty text.</param>
/// <param name="Markdown">The transcription. Empty for a blank page.</param>
/// <param name="ErrorMessage">A human sentence when <paramref name="Success"/> is false. Never contains the API key.</param>
public sealed record PageExtractionResult(bool Success, string Markdown, string? ErrorMessage)
{
    public static PageExtractionResult Ok(string markdown) => new(true, markdown, null);

    public static PageExtractionResult Failed(string message) => new(false, string.Empty, message);
}

// Response DTOs. Only the fields Lumen actually reads are modelled; System.Text.Json ignores
// the rest, so a change elsewhere in the API surface cannot break deserialisation.

public sealed class GeminiResponse
{
    [JsonPropertyName("candidates")]
    public List<GeminiCandidate>? Candidates { get; set; }

    [JsonPropertyName("promptFeedback")]
    public GeminiPromptFeedback? PromptFeedback { get; set; }
}

public sealed class GeminiCandidate
{
    [JsonPropertyName("content")]
    public GeminiContent? Content { get; set; }

    [JsonPropertyName("finishReason")]
    public string? FinishReason { get; set; }
}

public sealed class GeminiContent
{
    [JsonPropertyName("parts")]
    public List<GeminiPart>? Parts { get; set; }
}

public sealed class GeminiPart
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

public sealed class GeminiPromptFeedback
{
    /// <summary>Set when the request was refused outright, e.g. "SAFETY".</summary>
    [JsonPropertyName("blockReason")]
    public string? BlockReason { get; set; }
}
