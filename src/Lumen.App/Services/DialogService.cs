using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Lumen.App.Services;

public interface IDialogService
{
    string? OpenPdf();
    string? SaveExport(string suggestedFileName, string initialDirectory, out ExportFormat format);
    void OpenFile(string path);
    void ShowInFolder(string path);
    void OpenUrl(string url);
}

public enum ExportFormat
{
    Word,
    Markdown,
    PlainText
}

/// <summary>
/// Wraps the Windows common dialogs and shell verbs.
/// </summary>
/// <remarks>
/// Behind an interface so view models stay testable and so no view model has to reference
/// <c>Microsoft.Win32</c> directly.
/// </remarks>
public sealed class DialogService : IDialogService
{
    public string? OpenPdf()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a PDF",
            Filter = "PDF documents (*.pdf)|*.pdf",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveExport(string suggestedFileName, string initialDirectory, out ExportFormat format)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export extracted text",
            FileName = suggestedFileName,
            // Order matters: the first filter is the default, and Word is the primary path.
            Filter = "Word document (*.docx)|*.docx|Markdown (*.md)|*.md|Plain text (*.txt)|*.txt",
            FilterIndex = 1,
            AddExtension = true,
            OverwritePrompt = true
        };

        if (Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        if (dialog.ShowDialog() != true)
        {
            format = ExportFormat.Word;
            return null;
        }

        format = dialog.FilterIndex switch
        {
            2 => ExportFormat.Markdown,
            3 => ExportFormat.PlainText,
            _ => ExportFormat.Word
        };

        return dialog.FileName;
    }

    public void OpenFile(string path) => Shell(path);

    public void ShowInFolder(string path)
    {
        // /select, highlights the file inside its folder rather than just opening the folder.
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
        {
            UseShellExecute = true
        });
    }

    public void OpenUrl(string url) => Shell(url);

    private static void Shell(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
