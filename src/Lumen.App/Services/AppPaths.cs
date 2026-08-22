using System.IO;

namespace Lumen.App.Services;

/// <summary>
/// Where Lumen keeps its per-user state.
/// </summary>
/// <remarks>
/// Only settings and logs live here. PDF contents and extracted text are session-only and are
/// never written to disk, so uninstalling and deleting this directory can never lose a
/// document's content, only preferences and the stored key.
/// </remarks>
public static class AppPaths
{
    /// <summary><c>%APPDATA%\Lumen</c>.</summary>
    public static string AppData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Lumen");

    public static string Logs => Path.Combine(AppData, "logs");

    public static void EnsureCreated() => Directory.CreateDirectory(AppData);
}
