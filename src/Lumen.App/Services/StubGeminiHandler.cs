using System.Net;
using System.Net.Http;
using System.Text;

namespace Lumen.App.Services;

/// <summary>
/// Answers every Gemini request with a canned success instead of calling the real API. Activated
/// only when the environment variable <c>LUMEN_UI_TEST</c> is set to <c>1</c>, which the FlaUI
/// suite (<c>tests/Lumen.UiTests</c>) does before launching the published exe. This is what lets
/// that suite drive the real extraction pipeline — request building, response parsing, per-page
/// status, copy, export — through the actual UI without a real API key or network access.
/// </summary>
public sealed class StubGeminiHandler : HttpMessageHandler
{
    public const string StubMarkdown = "STUB TRANSCRIPTION: this text came from the offline UI-test double, not Gemini.";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = $$"""
            {
              "candidates": [
                {
                  "content": { "parts": [ { "text": {{System.Text.Json.JsonSerializer.Serialize(StubMarkdown)}} } ] },
                  "finishReason": "STOP"
                }
              ]
            }
            """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        return Task.FromResult(response);
    }
}
