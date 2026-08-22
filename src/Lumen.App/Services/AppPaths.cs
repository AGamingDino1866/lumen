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
    /// <summary>
    /// <c>%APPDATA%\Lumen</c>, or an isolated directory when <c>LUMEN_APPDATA_OVERRIDE</c> is
    /// set. The override exists solely so the FlaUI suite (<c>tests/Lumen.UiTests</c>) can run
    /// against a throwaway settings directory instead of a real user's saved API key and recent
    /// files — it has no effect unless that variable is explicitly set.
    /// </summary>
    public static string AppData { get; } =
        Environment.GetEnvironmentVariable("LUMEN_APPDATA_OVERRIDE") is { Length: > 0 } overridePath
            ? overridePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lumen");

    public static string Logs => Path.Combine(AppData, "logs");

    public static void EnsureCreated() => Directory.CreateDirectory(AppData);
}
