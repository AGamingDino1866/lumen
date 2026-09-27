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
    /// <summary>Message for a key the API refused, whatever status it arrived under.</summary>
    public const string KeyRejected =
        "That API key was rejected. Check or replace your key in Settings.";

    /// <summary>Message for a key whose project has not enabled the API.</summary>
    public const string ApiNotEnabled =
        "This key's Google project does not have the Generative Language API enabled. " +
        "Enable it in Google AI Studio, then try again.";

    public static string ToUserMessage(HttpStatusCode status) => ToUserMessage(status, null);

    /// <summary>
    /// Maps a failed response to a message, preferring the machine-readable reason in
    /// <paramref name="payload"/> over the bare status.
    /// </summary>
    /// <remarks>
    /// The status alone misdiagnoses the single most common first-run failure: Google reports a
    /// rejected key as <c>400 INVALID_ARGUMENT</c>, never 401, so a mistyped key would otherwise
    /// be reported as a malformed page and send the user looking at their PDF instead of their
    /// key. Only Google's fixed reason vocabulary is matched, never the free-text message, which
    /// is not a stable contract and is not guaranteed to be free of the request's own contents.
    /// </remarks>
    /// <param name="payload">The error response body, or null when there was none.</param>
    public static string ToUserMessage(HttpStatusCode status, string? payload)
    {
        if (payload is { Length: > 0 })
        {
            if (payload.Contains("API_KEY_INVALID", StringComparison.Ordinal) ||
                payload.Contains("API_KEY_SERVICE_BLOCKED", StringComparison.Ordinal))
            {
                return KeyRejected;
            }

            if (payload.Contains("SERVICE_DISABLED", StringComparison.Ordinal) ||
                payload.Contains("ACCESS_TOKEN_SCOPE_INSUFFICIENT", StringComparison.Ordinal))
            {
                return ApiNotEnabled;
            }
        }

        return FromStatus(status);
    }

    private static string FromStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest =>
            "Gemini rejected this page. The image may be too large or the page may be malformed.",

        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => KeyRejected,

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

    /// <summary>Message for a reply that ran out of output budget before the page ended.</summary>
    public const string Truncated =
        "Gemini ran out of room before the end of this page, so the transcription is incomplete. " +
        "Retry this page.";

    /// <summary>Message for a page the model declined to transcribe.</summary>
    public const string Blocked =
        "Gemini declined to transcribe this page. Its safety filters rejected the content.";
}
