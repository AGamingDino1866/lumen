namespace Lumen.Core.Settings;

/// <summary>
/// Everything Lumen persists between sessions.
/// </summary>
/// <remarks>
/// Deliberately absent: PDF contents and extracted text. Those are session-only and never
/// touch disk. The only sensitive value here is <see cref="ProtectedApiKey"/>, which is
/// DPAPI ciphertext, never plaintext.
/// </remarks>
public sealed class LumenSettings
{
    /// <summary>Schema version, so a future release can migrate rather than discard.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>DPAPI-encrypted API key, base64. Null when no key has been stored.</summary>
    public string? ProtectedApiKey { get; set; }

    /// <summary>Gemini model id. Flash is the right cost/quality point for OCR.</summary>
    public string Model { get; set; } = "gemini-2.5-flash";

    /// <summary>"System", "Light", or "Dark".</summary>
    public string ThemeMode { get; set; } = "System";

    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 820;

    /// <summary>
    /// Saved window position, or null when the window has never been positioned. Nullable rather
    /// than NaN: NaN is not representable in JSON and throws on serialisation.
    /// </summary>
    public double? WindowLeft { get; set; }

    /// <inheritdoc cref="WindowLeft"/>
    public double? WindowTop { get; set; }

    public bool Maximized { get; set; }

    /// <summary>Splitter position as a fraction of window width, 0-1.</summary>
    public double SplitterPosition { get; set; } = 0.52;

    /// <summary>Most recent first, capped at <see cref="RecentFilesLimit"/>.</summary>
    public List<string> RecentFiles { get; set; } = [];

    /// <summary>Insert a page break between source pages in the exported .docx.</summary>
    public bool ExportPageBreaks { get; set; } = true;

    /// <summary>Emit a "Page N" heading per source page in the exported .docx.</summary>
    public bool ExportPageHeadings { get; set; } = true;

    /// <summary>Show results as one merged document rather than per-page cards.</summary>
    public bool MergedResultsView { get; set; } = true;

    public const int RecentFilesLimit = 10;

    /// <summary>
    /// Records <paramref name="path"/> as the most recent file, removing any earlier entry for
    /// the same path and trimming the list to <see cref="RecentFilesLimit"/>.
    /// </summary>
    public void AddRecentFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);

        if (RecentFiles.Count > RecentFilesLimit)
        {
            RecentFiles.RemoveRange(RecentFilesLimit, RecentFiles.Count - RecentFilesLimit);
        }
    }
}
