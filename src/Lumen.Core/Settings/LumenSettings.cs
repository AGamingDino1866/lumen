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
    public string Model { get; set; } = DefaultModel;

    /// <summary>The model used unless the user picks another.</summary>
    public const string DefaultModel = "gemini-3.7-flash";

    /// <summary>
    /// Every model id Lumen will send. A value outside this set is reset to
    /// <see cref="DefaultModel"/> on load. The Settings window's model picker is built from this
    /// same array (see SettingsWindow.xaml.cs) rather than its own hardcoded copy, specifically
    /// so this is the one place that can go stale, not two.
    /// </summary>
    /// <remarks>
    /// This list is the whole reason the app can be trusted to start in a working state. The
    /// model id is part of the request URL, so a value that is not a real model is not a cosmetic
    /// problem: every page of every run returns 404 and the app appears completely broken. Google
    /// retires model ids over time -- the 2.x generation previously listed here is gone -- so this
    /// array needs the occasional refresh against Google's current model list, not just protection
    /// against a hand-edited or typo'd settings file.
    /// </remarks>
    public static readonly string[] SupportedModels =
    [
        "gemini-3.5-flash-lite",
        "gemini-3.7-flash",
        "gemini-3.1-pro-preview"
    ];

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
