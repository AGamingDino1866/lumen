using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.App.Services;
using Lumen.Core.Export;
using Lumen.Core.Gemini;
using Lumen.Core.Markdown;
using Lumen.Core.Pdf;
using Lumen.Core.Security;
using Lumen.Core.Selection;
using Lumen.Core.Settings;

namespace Lumen.App.ViewModels;

/// <summary>
/// Orchestrates the whole window: document, page grid, selection, extraction, results, export.
/// </summary>
/// <remarks>
/// Everything here is async to the bottom. There is no <c>.Result</c>, no <c>.Wait()</c>, and no
/// <c>async void</c>: the UI thread must stay responsive while up to four pages are rendering
/// and transcribing concurrently.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly ISecretStore _secrets;
    private readonly PdfRenderService _renderer;
    private readonly GeminiClient _gemini;
    private readonly ThumbnailQueue _thumbnails;
    private readonly IClipboardService _clipboard;
    private readonly IDialogService _dialogs;
    private readonly IThemeService _theme;
    private readonly IMotionService _motion;
    private readonly ILogService _log;
    private readonly IUpdateCheckService _updateCheck;

    private CancellationTokenSource? _extraction;
    private string? _documentPassword;
    private string? _apiKey;

    public MainViewModel(
        SettingsStore settingsStore,
        LumenSettings settings,
        ISecretStore secrets,
        PdfRenderService renderer,
        GeminiClient gemini,
        ThumbnailQueue thumbnails,
        IClipboardService clipboard,
        IDialogService dialogs,
        IThemeService theme,
        IMotionService motion,
        ILogService log,
        IUpdateCheckService updateCheck)
    {
        _settingsStore = settingsStore;
        _secrets = secrets;
        _renderer = renderer;
        _gemini = gemini;
        _thumbnails = thumbnails;
        _clipboard = clipboard;
        _dialogs = dialogs;
        _theme = theme;
        _motion = motion;
        _log = log;
        _updateCheck = updateCheck;

        Settings = settings;
        _apiKey = secrets.Unprotect(settings.ProtectedApiKey);
        _selectedModel = settings.Model;
        _isMergedView = settings.MergedResultsView;

        RecentFiles = new ObservableCollection<RecentFileViewModel>(
            settings.RecentFiles.Select(p => new RecentFileViewModel(p)));

        _thumbnails.Ready += OnThumbnailReady;
        _thumbnails.Failed += OnThumbnailFailed;
    }

    public LumenSettings Settings { get; }

    public ObservableCollection<PageTileViewModel> Pages { get; } = [];

    public ObservableCollection<PageResultViewModel> Results { get; } = [];

    public ObservableCollection<RecentFileViewModel> RecentFiles { get; }

    // ===================== Document state =====================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument))]
    [NotifyPropertyChangedFor(nameof(DocumentSummary))]
    private PdfDocumentInfo? _document;

    public bool HasDocument => Document is not null;

    /// <summary>Shown in the title bar: filename, page count, size.</summary>
    public string DocumentSummary => Document is null
        ? "No document open"
        : $"{Document.FileName}  ·  {Document.PageCount} pages  ·  {Document.DisplaySize}";

    // ===================== Selection =====================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionSummary))]
    [NotifyPropertyChangedFor(nameof(CanExtract))]
    [NotifyPropertyChangedFor(nameof(ExtractDisabledReason))]
    private int _selectedCount;

    /// <summary>The persistent bottom-bar count, e.g. "7 of 312 pages selected".</summary>
    public string SelectionSummary => Document is null
        ? "Open a PDF to begin"
        : $"{SelectedCount} of {Document.PageCount} pages selected";

    [ObservableProperty]
    private string _pageRangeText = string.Empty;

    [ObservableProperty]
    private string? _pageRangeError;

    // ===================== Extraction =====================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExtract))]
    [NotifyPropertyChangedFor(nameof(ExtractDisabledReason))]
    private bool _isExtracting;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string _selectedModel;

    public bool CanExtract => HasDocument && SelectedCount > 0 && !IsExtracting;

    /// <summary>
    /// Explains a disabled primary button rather than leaving the user to guess. Surfaced as the
    /// button's tooltip.
    /// </summary>
    public string? ExtractDisabledReason
    {
        get
        {
            if (IsExtracting) return "Extraction is already running.";
            if (!HasDocument) return "Open a PDF first.";
            if (SelectedCount == 0) return "Select at least one page to extract.";
            return null;
        }
    }

    // ===================== Results =====================

    [ObservableProperty]
    private bool _isMergedView;

    [ObservableProperty]
    private string _findText = string.Empty;

    public bool HasResults => Results.Count > 0;

    // ===================== API key =====================

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public string MaskedApiKey => _apiKey is { Length: > 8 }
        ? $"{_apiKey[..4]}{new string('•', 12)}{_apiKey[^4..]}"
        : "Not set";

    /// <summary>
    /// Validates a key against the API, and persists it only if the call succeeds. The result
    /// carries the real reason on failure (invalid key, rate limited, network problem, ...) so
    /// the gate can show something more useful than a single generic "not accepted" message.
    /// </summary>
    public async Task<PageExtractionResult> TrySetApiKeyAsync(string candidate, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return PageExtractionResult.Failed("Enter a key first.");
        }

        var result = await _gemini.ValidateKeyAsync(candidate.Trim(), SelectedModel, cancellationToken)
            .ConfigureAwait(true);

        if (!result.Success)
        {
            return result;
        }

        _apiKey = candidate.Trim();
        Settings.ProtectedApiKey = _secrets.Protect(_apiKey);
        _settingsStore.Save(Settings);

        OnPropertyChanged(nameof(HasApiKey));
        OnPropertyChanged(nameof(MaskedApiKey));
        return result;
    }

    public void ClearApiKey()
    {
        _apiKey = null;
        Settings.ProtectedApiKey = null;
        _settingsStore.Save(Settings);

        OnPropertyChanged(nameof(HasApiKey));
        OnPropertyChanged(nameof(MaskedApiKey));
    }

    // ===================== Opening documents =====================

    [RelayCommand]
    private void Open()
    {
        var path = _dialogs.OpenPdf();

        if (path is not null)
        {
            LoadDocument(path);
        }
    }

    /// <summary>
    /// Opens a document. Rejects non-PDFs inline rather than through a message box, and reports a
    /// password requirement instead of throwing.
    /// </summary>
    public void LoadDocument(string path, string? password = null)
    {
        StatusMessage = null;

        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Lumen opens PDF files. That one is not a PDF.";
            return;
        }

        try
        {
            var info = _renderer.Open(path, password);

            _documentPassword = password;
            Document = info;

            Pages.Clear();
            Results.Clear();
            SelectedCount = 0;
            PageRangeText = string.Empty;
            PageRangeError = null;

            for (var i = 0; i < info.PageCount; i++)
            {
                var size = i < info.PageSizes.Count ? info.PageSizes[i] : new PdfPageSize(595, 842);
                Pages.Add(new PageTileViewModel(i, size.AspectRatio));
            }

            _thumbnails.SetDocument(path, password);

            Settings.AddRecentFile(path);
            _settingsStore.Save(Settings);
            RefreshRecentFiles();

            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(HasResults));

            _log.Info($"Opened a document with {info.PageCount} pages.");
        }
        catch (PdfPasswordRequiredException)
        {
            PasswordRequired?.Invoke(this, path);
        }
        catch (PdfUnreadableException ex)
        {
            _log.Error("Failed to open a PDF.", ex);
            StatusMessage = "That PDF could not be opened. It may be damaged.";
        }
        catch (FileNotFoundException)
        {
            StatusMessage = "That file no longer exists.";
            RemoveRecentFile(path);
        }
    }

    /// <summary>Raised when an encrypted document needs a password from the user.</summary>
    public event EventHandler<string>? PasswordRequired;

    private void RefreshRecentFiles()
    {
        RecentFiles.Clear();

        foreach (var path in Settings.RecentFiles)
        {
            RecentFiles.Add(new RecentFileViewModel(path));
        }
    }

    private void RemoveRecentFile(string path)
    {
        Settings.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _settingsStore.Save(Settings);
        RefreshRecentFiles();
    }

    [RelayCommand]
    private void OpenRecent(RecentFileViewModel? file)
    {
        if (file is not null)
        {
            LoadDocument(file.Path);
        }
    }

    [RelayCommand]
    private void ClearRecentFiles()
    {
        Settings.RecentFiles.Clear();
        _settingsStore.Save(Settings);
        RefreshRecentFiles();
    }

    // ===================== Selection commands =====================

    /// <summary>
    /// Keyboard select-all. Uses the silent setter so a 312-page document fires zero badge
    /// storyboards: animating an action the user repeats constantly makes the app feel slow.
    /// </summary>
    [RelayCommand]
    private void SelectAll()
    {
        foreach (var page in Pages)
        {
            page.SetSelectedSilently(true);
        }

        RecalculateSelection();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var page in Pages)
        {
            page.SetSelectedSilently(false);
        }

        RecalculateSelection();
    }

    /// <summary>Applies the page-range textbox. Reports problems inline and never throws.</summary>
    [RelayCommand]
    private void ApplyPageRange()
    {
        if (Document is null)
        {
            return;
        }

        var result = PageRangeParser.Parse(PageRangeText, Document.PageCount);

        if (!result.IsValid)
        {
            PageRangeError = result.Error;
            return;
        }

        PageRangeError = null;

        foreach (var page in Pages)
        {
            page.SetSelectedSilently(result.Pages.Contains(page.PageNumber));
        }

        RecalculateSelection();
    }

    public void RecalculateSelection()
    {
        SelectedCount = Pages.Count(p => p.IsSelected);
        OnPropertyChanged(nameof(SelectionSummary));
    }

    // ===================== Extraction =====================

    [RelayCommand]
    private async Task ExtractAsync()
    {
        if (!CanExtract || Document is null || _apiKey is null)
        {
            return;
        }

        var selected = Pages.Where(p => p.IsSelected).Select(p => p.PageIndex).OrderBy(i => i).ToList();

        _extraction = new CancellationTokenSource();
        var token = _extraction.Token;

        IsExtracting = true;
        StatusMessage = null;
        Results.Clear();
        ProgressValue = 0;

        foreach (var index in selected)
        {
            Results.Add(new PageResultViewModel(index + 1));
        }

        OnPropertyChanged(nameof(HasResults));

        var prompt = ExtractionPromptLoader.Load();
        var completed = 0;
        var total = selected.Count;

        ProgressText = $"Extracting page 1 of {total}";

        // A modest degree of parallelism: enough to hide latency, few enough to stay clear of
        // rate limits. GeminiClient enforces its own ceiling as well.
        using var gate = new SemaphoreSlim(4, 4);

        var tasks = selected.Select(async pageIndex =>
        {
            await gate.WaitAsync(token).ConfigureAwait(true);

            var result = Results.First(r => r.PageNumber == pageIndex + 1);

            try
            {
                token.ThrowIfCancellationRequested();
                result.Status = PageStatus.Running;

                await ExtractPageAsync(result, pageIndex, prompt, token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                result.Status = PageStatus.Queued;
            }
            finally
            {
                gate.Release();

                completed++;
                ProgressValue = total == 0 ? 0 : completed * 100.0 / total;
                ProgressText = completed >= total
                    ? $"Extracted {total} of {total} pages"
                    : $"Extracting page {Math.Min(completed + 1, total)} of {total}";
            }
        });

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Extraction cancelled.";
        }
        finally
        {
            IsExtracting = false;
            _extraction?.Dispose();
            _extraction = null;
        }

        var failed = Results.Count(r => r.IsFailed);
        if (failed > 0)
        {
            StatusMessage = failed == 1
                ? "One page failed. Use its retry button to try again."
                : $"{failed} pages failed. Use their retry buttons to try again.";
        }
    }

    private async Task ExtractPageAsync(
        PageResultViewModel result, int pageIndex, string prompt, CancellationToken token)
    {
        try
        {
            // Rendering is CPU-bound and must not run on the UI thread.
            var png = await Task.Run(
                () => _renderer.RenderPagePng(
                    Document!.FilePath,
                    pageIndex,
                    dpi: 150,
                    maxLongEdgePixels: 2048,
                    password: _documentPassword),
                token).ConfigureAwait(true);

            var response = await _gemini
                .ExtractAsync(png, SelectedModel, _apiKey!, prompt, token)
                .ConfigureAwait(true);

            if (response.Success)
            {
                result.SetResult(response.Markdown);
            }
            else
            {
                result.SetFailed(response.ErrorMessage ?? "This page could not be extracted.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One page failing must never end the run.
            _log.Error($"Extraction failed for page {pageIndex + 1}.", ex);
            result.SetFailed("This page could not be extracted.");
        }
    }

    [RelayCommand]
    private void CancelExtraction()
    {
        _extraction?.Cancel();
        StatusMessage = "Cancelling…";
    }

    [RelayCommand]
    private async Task RetryPageAsync(PageResultViewModel? result)
    {
        if (result is null || Document is null || _apiKey is null || IsExtracting)
        {
            return;
        }

        result.Status = PageStatus.Running;

        using var cts = new CancellationTokenSource();
        await ExtractPageAsync(result, result.PageNumber - 1, ExtractionPromptLoader.Load(), cts.Token)
            .ConfigureAwait(true);
    }

    // ===================== Copy =====================

    [RelayCommand]
    private void CopyPage(PageResultViewModel? result)
    {
        if (result is null)
        {
            return;
        }

        Copy(result.Markdown);
    }

    [RelayCommand]
    private void CopyAllFormatted() => Copy(BuildAll(ExportFormat.Markdown));

    [RelayCommand]
    private void CopyAllMarkdown() => Copy(BuildAll(ExportFormat.Markdown));

    [RelayCommand]
    private void CopyAllPlainText() => Copy(BuildAll(ExportFormat.PlainText));

    private void Copy(string text)
    {
        if (!_clipboard.TrySetText(text, out var error))
        {
            StatusMessage = error;
        }
    }

    private string BuildAll(ExportFormat format)
    {
        var pages = CompletedPages();

        return format == ExportFormat.PlainText
            ? TextExporters.ToPlainText(pages, Settings.ExportPageHeadings)
            : TextExporters.ToMarkdown(pages, Settings.ExportPageHeadings);
    }

    private List<ExportPage> CompletedPages() =>
        Results.Where(r => r.Status == PageStatus.Done)
               .Select(r => new ExportPage(r.PageNumber, r.Document))
               .ToList();

    // ===================== Export =====================

    /// <summary>
    /// Whether an export can currently be started, checked by the view before it shows the
    /// export-options dialog (page breaks / heading-per-page checkboxes).
    /// </summary>
    public bool CanExport => Document is not null && Results.Count > 0;

    /// <summary>
    /// Runs the export once the view has collected the page-break and page-heading choices from
    /// its dialog and the target path/format from the Save dialog. Split from the option and
    /// path prompts (both view concerns owned by the code-behind) so this method stays a plain,
    /// testable write operation.
    /// </summary>
    public void Export(bool pageBreaks, bool pageHeadings)
    {
        if (!CanExport)
        {
            return;
        }

        var suggested = $"{Path.GetFileNameWithoutExtension(Document!.FileName)}-extracted.docx";
        var directory = Path.GetDirectoryName(Document.FilePath) ?? string.Empty;

        var target = _dialogs.SaveExport(suggested, directory, out var format);

        if (target is null)
        {
            return;
        }

        // The choice made in the dialog is remembered as the default for next time.
        Settings.ExportPageBreaks = pageBreaks;
        Settings.ExportPageHeadings = pageHeadings;
        _settingsStore.Save(Settings);

        try
        {
            WriteExport(target, format, pageBreaks, pageHeadings);

            StatusMessage = $"Exported to {Path.GetFileName(target)}.";
            ExportCompleted?.Invoke(this, target);
        }
        catch (IOException ex)
        {
            // The overwhelmingly common cause is the file being open in Word.
            _log.Error("Export failed because the target file was locked.", ex);
            StatusMessage = "That file is open in another application. Close it and try again.";
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.Error("Export failed due to permissions.", ex);
            StatusMessage = "Lumen does not have permission to write there. Choose another folder.";
        }
    }

    private void WriteExport(string target, ExportFormat format, bool pageBreaks, bool pageHeadings)
    {
        var pages = CompletedPages();

        switch (format)
        {
            case ExportFormat.Word:
                using (var stream = File.Create(target))
                {
                    WordExporter.Write(stream, pages, new WordExportOptions(
                        pageBreaks,
                        pageHeadings,
                        Document!.FileName));
                }
                break;

            case ExportFormat.Markdown:
                File.WriteAllText(target, TextExporters.ToMarkdown(pages, pageHeadings));
                break;

            case ExportFormat.PlainText:
                File.WriteAllText(target, TextExporters.ToPlainText(pages, pageHeadings));
                break;
        }
    }

    /// <summary>Raised after a successful export so the view can offer to open or reveal it.</summary>
    public event EventHandler<string>? ExportCompleted;

    // ===================== Theme and settings =====================

    [RelayCommand]
    private void ToggleTheme()
    {
        var next = _theme.IsDark ? ThemeMode.Light : ThemeMode.Dark;

        _theme.Apply(next);
        Settings.ThemeMode = next.ToString();
        _settingsStore.Save(Settings);
    }

    public void PersistSettings() => _settingsStore.Save(Settings);

    partial void OnSelectedModelChanged(string value)
    {
        Settings.Model = value;
        _settingsStore.Save(Settings);
    }

    partial void OnIsMergedViewChanged(bool value)
    {
        Settings.MergedResultsView = value;
        _settingsStore.Save(Settings);
    }

    // ===================== Thumbnails =====================

    /// <summary>Requests a thumbnail for a page that has scrolled into view.</summary>
    public void RequestThumbnail(int pageIndex, int pixelWidth) =>
        _thumbnails.Request(pageIndex, pixelWidth);

    /// <summary>
    /// Renders a page at preview resolution. Called from a background thread by the preview
    /// window, which freezes the resulting bitmap before it reaches the UI thread.
    /// </summary>
    public byte[] RenderPreview(int pageIndex) =>
        _renderer.RenderPagePng(
            Document!.FilePath,
            pageIndex,
            dpi: 200,
            maxLongEdgePixels: 3000,
            password: _documentPassword);

    private void OnThumbnailReady(object? sender, ThumbnailReady e)
    {
        // The bitmap is already frozen, so marshalling it to the UI thread is safe.
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (e.PageIndex < Pages.Count)
            {
                Pages[e.PageIndex].Thumbnail = e.Image;
            }
        });
    }

    private void OnThumbnailFailed(object? sender, ThumbnailFailed e)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (e.PageIndex < Pages.Count)
            {
                Pages[e.PageIndex].IsRenderFailed = true;
                Pages[e.PageIndex].RenderError = e.Message;
            }
        });
    }

    public void Dispose()
    {
        _thumbnails.Ready -= OnThumbnailReady;
        _thumbnails.Failed -= OnThumbnailFailed;
        _thumbnails.Dispose();
        _extraction?.Dispose();
        _gemini.Dispose();
    }
}

/// <summary>An entry in the start screen's recent files list.</summary>
public sealed class RecentFileViewModel
{
    public RecentFileViewModel(string path)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        Directory = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
    }

    public string Path { get; }
    public string FileName { get; }
    public string Directory { get; }
}
