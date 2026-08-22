using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace Lumen.App.Services;

public interface IClipboardService
{
    bool TrySetText(string text, out string? error);
}

/// <summary>
/// Copies text to the Windows clipboard, retrying briefly when another process holds it.
/// </summary>
/// <remarks>
/// The Windows clipboard is a single system-wide resource opened exclusively. When another
/// application has it open, <see cref="Clipboard.SetText(string)"/> throws
/// <see cref="COMException"/>. That is a transient, extremely common condition, so a few short
/// retries almost always succeed, and a clipboard hiccup must never take down the application.
/// </remarks>
public sealed class ClipboardService : IClipboardService
{
    private const int MaxAttempts = 3;
    private const int RetryDelayMs = 50;

    private readonly ILogService _log;

    public ClipboardService(ILogService log) => _log = log;

    public bool TrySetText(string text, out string? error)
    {
        error = null;

        if (string.IsNullOrEmpty(text))
        {
            error = "There is nothing to copy yet.";
            return false;
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                // The copy=true overload flushes the data so it survives Lumen closing.
                Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (COMException ex)
            {
                if (attempt == MaxAttempts)
                {
                    _log.Error("Clipboard unavailable after retries.", ex);
                    error = "Another application is using the clipboard. Try again in a moment.";
                    return false;
                }

                Thread.Sleep(RetryDelayMs);
            }
            catch (Exception ex)
            {
                _log.Error("Unexpected clipboard failure.", ex);
                error = "Lumen could not copy to the clipboard.";
                return false;
            }
        }

        return false;
    }
}
