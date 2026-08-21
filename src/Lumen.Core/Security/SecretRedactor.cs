using System.Text.RegularExpressions;

namespace Lumen.Core.Security;

/// <summary>
/// Strips anything key-shaped out of a string before it reaches a log file.
/// </summary>
/// <remarks>
/// Every line written by the logger passes through <see cref="Redact"/>. This is a defence in
/// depth measure: the application is written never to log the key in the first place, but a
/// stack trace, a serialised request, or a third-party exception message can carry one by
/// accident, and a log file is exactly the artefact a user pastes into a bug report.
/// </remarks>
public static partial class SecretRedactor
{
    private const string Replacement = "[REDACTED]";

    /// <summary>
    /// Google API keys have a stable, recognisable shape: the literal "AIza" followed by
    /// base64url characters. Matching the shape catches keys wherever they appear, including
    /// inside a URL or an exception message.
    /// </summary>
    [GeneratedRegex(@"AIza[0-9A-Za-z_\-]{10,}", RegexOptions.CultureInvariant)]
    private static partial Regex GoogleKeyPattern();

    /// <summary>
    /// Catches assignments such as <c>api_key=...</c>, <c>apiKey: ...</c>, and the
    /// <c>x-goog-api-key</c> header, so a key that does not match the Google shape
    /// (a placeholder, or a future key format) is still removed.
    /// </summary>
    [GeneratedRegex(
        @"(?<label>(?:x-goog-)?api[-_]?key|key)\s*[:=]\s*(?<value>[^\s,;""'}\]]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyAssignmentPattern();

    /// <summary>
    /// Returns <paramref name="line"/> with any key-shaped content replaced. Never throws;
    /// null becomes an empty string.
    /// </summary>
    public static string Redact(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        var redacted = GoogleKeyPattern().Replace(line, Replacement);

        redacted = KeyAssignmentPattern().Replace(
            redacted,
            match => match.Groups["value"].Value == Replacement
                ? match.Value
                : $"{match.Groups["label"].Value}={Replacement}");

        return redacted;
    }
}
