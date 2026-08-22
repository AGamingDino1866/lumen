using System.IO;
using System.Text;
using Lumen.Core.Security;

namespace Lumen.App.Services;

public interface ILogService
{
    void Info(string message);
    void Error(string message, Exception? exception = null);
    string LogDirectory { get; }
}

/// <summary>
/// Appends redacted lines to a daily log file under <c>%APPDATA%\Lumen\logs</c>.
/// </summary>
/// <remarks>
/// Every line passes through <see cref="SecretRedactor"/> before it is written. Lumen is written
/// never to log the API key, but a stack trace or a third-party exception message can carry one
/// by accident, and a log file is exactly the artefact a user pastes into a bug report.
/// <para>
/// Logging must never itself throw. A failure to write a diagnostic is not worth taking the
/// application down for, so every write is best effort.
/// </para>
/// </remarks>
public sealed class LogService : ILogService
{
    private const int RetentionDays = 14;
    private readonly object _gate = new();

    public LogService(string appDataDirectory)
    {
        LogDirectory = Path.Combine(appDataDirectory, "logs");
        TryPruneOldLogs();
    }

    public string LogDirectory { get; }

    public void Info(string message) => Write("INFO ", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);

            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append("  ")
                .Append(level)
                .Append("  ")
                .Append(SecretRedactor.Redact(message));

            if (exception is not null)
            {
                builder.AppendLine()
                       .Append("    ")
                       .Append(SecretRedactor.Redact(exception.GetType().FullName))
                       .Append(": ")
                       .Append(SecretRedactor.Redact(exception.Message))
                       .AppendLine()
                       .Append(SecretRedactor.Redact(exception.StackTrace ?? "(no stack trace)"));
            }

            builder.AppendLine();

            var path = Path.Combine(LogDirectory, $"lumen-{DateTime.Now:yyyy-MM-dd}.log");

            lock (_gate)
            {
                File.AppendAllText(path, builder.ToString(), Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            // Diagnostics are not worth crashing for.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void TryPruneOldLogs()
    {
        try
        {
            if (!Directory.Exists(LogDirectory))
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);

            foreach (var file in Directory.EnumerateFiles(LogDirectory, "lumen-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
