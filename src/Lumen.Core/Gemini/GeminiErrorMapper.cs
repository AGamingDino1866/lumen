using System.Net;

namespace Lumen.Core.Gemini;

/// <summary>
/// Turns an HTTP status into one plain sentence the user can act on.
/// </summary>
/// <remarks>
/// Nothing here mentions status codes, exception types, or stack traces, and nothing here can
/// contain the API key, because the key is never an input. Every message says what happened and,
/// where there is one, what to do about it.
/// </remarks>
public static class GeminiErrorMapper
{
    public static string ToUserMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest =>
            "Gemini rejected this page. The image may be too large or the page may be malformed.",

        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            "That API key was rejected. Check or replace your key in Settings.",

        HttpStatusCode.TooManyRequests =>
            "Gemini is rate limiting this key. Wait a moment and retry the remaining pages.",

        HttpStatusCode.RequestEntityTooLarge =>
            "This page produced too large an image to send. Try again at a lower resolution.",

        HttpStatusCode.RequestTimeout =>
            "The request to Gemini timed out. Check your connection and retry this page.",

        HttpStatusCode.NotFound =>
            "The selected model isn't available for this key. Google periodically retires model " +
            "versions — try a different model in Settings.",

        _ when (int)status >= 500 =>
            "Gemini is currently unavailable. This is a problem on Google's side, so try again shortly.",

        // The specific code is included here, unlike every case above: those are all common,
        // well-understood outcomes with a clear next step, but reaching this branch means
        // something genuinely unanticipated happened, and the code is the one piece of evidence
        // that turns "try again" into an actual diagnosis next time.
        _ =>
            $"Gemini could not process this page (HTTP {(int)status}). Retry it, or continue with the other pages."
    };

    /// <summary>Message for a response that arrived but could not be understood.</summary>
    public const string MalformedResponse =
        "Gemini returned a response Lumen could not read. Retry this page.";

    /// <summary>Message for a network-level failure before any response arrived.</summary>
    public const string NetworkFailure =
        "Lumen could not reach Gemini. Check your internet connection and retry.";

    /// <summary>Message for a page the model declined to transcribe.</summary>
    public const string Blocked =
        "Gemini declined to transcribe this page. Its safety filters rejected the content.";
}
