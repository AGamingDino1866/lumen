using System.Text.Json;

namespace Lumen.Core.Gemini;

/// <summary>
/// Builds the JSON body for a single-page <c>generateContent</c> request.
/// </summary>
/// <remarks>
/// The API key is deliberately absent from everything this class produces. Authentication is
/// the <c>x-goog-api-key</c> header, applied by <see cref="GeminiClient"/>; putting a key in a
/// URL or body risks it landing in a proxy log.
/// </remarks>
public static class ExtractionRequestBuilder
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false };

    /// <summary>
    /// Builds the request body carrying <paramref name="prompt"/> followed by the page image.
    /// </summary>
    /// <param name="pngBytes">The rendered page, PNG encoded.</param>
    /// <param name="prompt">The extraction instruction. Sent before the image.</param>
    public static string BuildJson(byte[] pngBytes, string prompt)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        ArgumentNullException.ThrowIfNull(prompt);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();

            writer.WriteStartArray("contents");
            writer.WriteStartObject();
            writer.WriteStartArray("parts");

            // Order matters: the instruction must precede the image so the model reads it as
            // a directive about the image rather than a caption after the fact.
            writer.WriteStartObject();
            writer.WriteString("text", prompt);
            writer.WriteEndObject();

            writer.WriteStartObject();
            writer.WriteStartObject("inline_data");
            writer.WriteString("mime_type", "image/png");
            writer.WriteBase64String("data", pngBytes);
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            // Transcription must be deterministic. Any temperature above zero invites the model
            // to paraphrase, which is precisely the failure this tool cannot tolerate.
            writer.WriteStartObject("generationConfig");
            writer.WriteNumber("temperature", 0);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
