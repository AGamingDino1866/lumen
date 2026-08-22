using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Core.Markdown;

namespace Lumen.App.ViewModels;

public enum PageStatus
{
    Queued,
    Running,
    Done,
    Failed
}

/// <summary>
/// One page's transcription result.
/// </summary>
/// <remarks>
/// In the merged results view these do not render as cards. Each becomes a section of continuous
/// flow separated by a hairline "Page N" divider, and that divider is the anchor: grid-to-result
/// scroll sync targets it, and hovering or keyboard-focusing it reveals this page's copy and
/// retry actions. Per-page recovery therefore survives the merge.
/// </remarks>
public sealed partial class PageResultViewModel : ObservableObject
{
    public PageResultViewModel(int pageNumber)
    {
        PageNumber = pageNumber;
        Document = MarkdownDocument.Empty;
    }

    public int PageNumber { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(HasContent))]
    private PageStatus _status = PageStatus.Queued;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContent))]
    [NotifyPropertyChangedFor(nameof(IsBlank))]
    private string _markdown = string.Empty;

    [ObservableProperty]
    private MarkdownDocument _document;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Status carries a word, not only a colour. Colour alone fails for a colour-blind user and
    /// fails again in a screenshot pasted into a bug report.
    /// </summary>
    public string StatusLabel => Status switch
    {
        PageStatus.Queued => "Queued",
        PageStatus.Running => "Extracting",
        PageStatus.Done => "Done",
        PageStatus.Failed => "Failed",
        _ => string.Empty
    };

    public bool IsFailed => Status == PageStatus.Failed;

    public bool IsBusy => Status is PageStatus.Queued or PageStatus.Running;

    public bool HasContent => Status == PageStatus.Done && Markdown.Length > 0;

    /// <summary>
    /// A page that succeeded but produced nothing. The prompt instructs the model to return
    /// empty rather than invent content, so this is a normal outcome worth showing explicitly
    /// instead of leaving an unexplained gap.
    /// </summary>
    public bool IsBlank => Status == PageStatus.Done && Markdown.Length == 0;

    public string AutomationName => $"Page {PageNumber}, {StatusLabel}";

    public void SetResult(string markdown)
    {
        Markdown = markdown;
        Document = MarkdownParser.Parse(markdown);
        ErrorMessage = null;
        Status = PageStatus.Done;
    }

    public void SetFailed(string message)
    {
        ErrorMessage = message;
        Status = PageStatus.Failed;
    }
}
