using System.Reflection;
using System.Text;

namespace Lumen.Core.Gemini;

/// <summary>
/// Loads the page-extraction instruction from the embedded <c>extraction-prompt.txt</c>.
/// </summary>
/// <remarks>
/// The prompt file documents each clause inline using <c>#!</c>-prefixed commentary lines, which
/// are stripped here. That keeps the rationale for every instruction next to the instruction
/// itself, so the prompt stays tunable without reading the source, while never spending tokens
/// sending commentary to the model.
/// </remarks>
public static class ExtractionPromptLoader
{
    private const string CommentPrefix = "#!";
    private const string ResourceName = "Lumen.Core.Gemini.extraction-prompt.txt";

    // Read once per process. The prompt is identical for every page of every run.
    private static readonly Lazy<string> Cached = new(ReadAndStrip, isThreadSafe: true);

    /// <summary>Returns the prompt text with all commentary removed.</summary>
    public static string Load() => Cached.Value;

    private static string ReadAndStrip()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' is missing. Check the EmbeddedResource item in Lumen.Core.csproj.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var raw = reader.ReadToEnd();

        var builder = new StringBuilder(raw.Length);
        var lastLineWasBlank = false;

        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.TrimStart().StartsWith(CommentPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var isBlank = line.Trim().Length == 0;

            // Stripping commentary leaves runs of blank lines behind. Collapse them so the
            // prompt stays compact, but keep single blank lines as paragraph separators.
            if (isBlank && lastLineWasBlank)
            {
                continue;
            }

            builder.Append(line).Append('\n');
            lastLineWasBlank = isBlank;
        }

        return builder.ToString().Trim();
    }
}
