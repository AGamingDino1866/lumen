using System.Net;
using System.Net.Http;
using System.Text;

namespace Lumen.App.Services;

/// <summary>
/// Answers every Gemini request with a canned success instead of calling the real API. Activated
/// only when the environment variable <c>LUMEN_UI_TEST</c> is set to <c>1</c>, which the FlaUI
/// suite (<c>tests/Lumen.UiTests</c>) and the screenshot tool (<c>tools/ScreenshotTool</c>) both
/// do before launching the built exe. This is what lets both drive the real extraction pipeline —
/// request building, response parsing, per-page status, copy, export — through the actual UI
/// without a real API key or network access.
/// </summary>
public sealed class StubGeminiHandler : HttpMessageHandler
{
    public const string StubMarkdown = """
        # Quarterly Summary

        This is a stand-in transcription produced by the offline UI-test double, not by Gemini —
        it exists so the extraction pipeline can be driven end to end without a real API key or
        network access.

        - Revenue grew **12%** quarter over quarter
        - Three new markets opened in the region
        - [Figure: bar chart comparing quarterly revenue across four regions]
        """;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Set by the screenshot tool only, so a "extraction in progress" capture has time to
        // land before the stub responds. Never set by the FlaUI suite, which wants speed.
        if (Environment.GetEnvironmentVariable("LUMEN_UI_TEST_DELAY_MS") is { Length: > 0 } delayText &&
            int.TryParse(delayText, out var delayMs) && delayMs > 0)
        {
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

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

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
