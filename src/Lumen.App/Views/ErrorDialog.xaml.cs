using System.Diagnostics;
using System.IO;
using System.Windows;
using Lumen.Core.Security;

namespace Lumen.App.Views;

/// <summary>
/// Shown when an unhandled exception is caught. Deliberately recoverable: the user continues
/// rather than losing their session.
/// </summary>
public partial class ErrorDialog : Window
{
    private readonly string _logDirectory;

    public ErrorDialog(Exception exception, string logDirectory)
    {
        InitializeComponent();

        _logDirectory = logDirectory;

        // The message is redacted on the way to the screen for the same reason it is redacted on
        // the way to the log: a user photographs or pastes this dialog into a bug report.
        DetailText.Text = SecretRedactor.Redact($"{exception.GetType().Name}: {exception.Message}");
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);

            Process.Start(new ProcessStartInfo(_logDirectory) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Opening Explorer is a convenience; failing to do so must not raise a second error
            // dialog on top of this one.
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
