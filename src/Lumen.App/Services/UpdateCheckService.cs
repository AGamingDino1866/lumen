using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;
using Lumen.Core.Updates;

namespace Lumen.App.Services;

/// <summary>A newer release, or none. <see cref="InstallerUrl"/> is null if the release has no
/// asset named like an installer, in which case the caller can only point at <see cref="ReleaseUrl"/>.</summary>
public sealed record UpdateCheckResult(bool IsAvailable, string? Version, string? ReleaseUrl, string? InstallerUrl);

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken);

    Task DownloadInstallerAsync(string url, string destinationPath, IProgress<double> progress, CancellationToken cancellationToken);

    void LaunchInstallerAndExit(string installerPath);
}

/// <summary>
/// Polls the GitHub Releases API for a newer Lumen release, downloads the installer asset with
/// progress reporting, and hands off to it. There is no signature verification here -- the
/// installer is trusted because it comes from Lumen's own GitHub Releases over HTTPS, the same
/// trust basis as downloading it by hand from the releases page.
/// </summary>
public sealed class UpdateCheckService(HttpClient http) : IUpdateCheckService
{
    private const string Owner = "AGamingDino1866";
    private const string Repo = "lumen";

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

            // The GitHub API rejects requests with no User-Agent outright.
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Lumen", CurrentVersion));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return None;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            var tag = doc.RootElement.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            var url = doc.RootElement.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;

            if (!UpdateVersion.IsNewer(CurrentVersion, tag))
            {
                return None;
            }

            var installerUrl = doc.RootElement.TryGetProperty("assets", out var assets)
                ? assets.EnumerateArray()
                    .Select(AssetDownloadUrlIfInstaller)
                    .FirstOrDefault(u => u is not null)
                : null;

            return new UpdateCheckResult(true, tag, url, installerUrl);
        }
        catch (Exception)
        {
            // A failed check must never disrupt startup; the user just doesn't see a prompt.
            return None;
        }
    }

    public async Task DownloadInstallerAsync(
        string url, string destinationPath, IProgress<double> progress, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(destinationPath);

        var buffer = new byte[81920];
        long readSoFar = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readSoFar += read;

            if (total is > 0)
            {
                progress.Report(readSoFar * 100.0 / total.Value);
            }
        }
    }

    /// <summary>
    /// Launches the downloaded installer silently and exits Lumen immediately after. Lumen
    /// cannot wait around for the installer to finish and then relaunch itself, because it must
    /// have already exited before the installer can replace its own locked exe -- so a short-
    /// lived, invisible PowerShell watcher does the waiting and relaunching instead, entirely
    /// outside Lumen's own lifetime.
    /// </summary>
    public void LaunchInstallerAndExit(string installerPath)
    {
        var installer = Process.Start(new ProcessStartInfo(
            installerPath, "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES")
        {
            UseShellExecute = false
        });

        if (installer is not null)
        {
            var exePath = Path.Combine(
                Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory,
                "Lumen.exe");

            Process.Start(new ProcessStartInfo("powershell.exe",
                $"-WindowStyle Hidden -NoProfile -Command " +
                $"\"Wait-Process -Id {installer.Id} -ErrorAction SilentlyContinue; " +
                $"Start-Process -FilePath '{exePath}'\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }

        Application.Current.Shutdown();
    }

    private static string? AssetDownloadUrlIfInstaller(JsonElement asset)
    {
        if (!asset.TryGetProperty("name", out var nameEl) ||
            !asset.TryGetProperty("browser_download_url", out var urlEl))
        {
            return null;
        }

        var name = nameEl.GetString() ?? string.Empty;

        var looksLikeInstaller =
            name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            name.Contains("Setup", StringComparison.OrdinalIgnoreCase);

        return looksLikeInstaller ? urlEl.GetString() : null;
    }

    private static readonly UpdateCheckResult None = new(false, null, null, null);

    // Assembly.Location is always empty for a single-file publish (Lumen's shipping form) --
    // Environment.ProcessPath is what actually resolves in both that and a normal Debug build.
    // The override exists solely to verify the download/install/relaunch cycle end to end
    // against a real published release without needing a second real version bump to test
    // against -- set only by hand, for that one-off check, never by the shipped app.
    private static string CurrentVersion =>
        Environment.GetEnvironmentVariable("LUMEN_UPDATE_TEST_CURRENT_VERSION") is { Length: > 0 } overridden
            ? overridden
            : Environment.ProcessPath is { } path
                ? FileVersionInfo.GetVersionInfo(path).FileVersion ?? "0.0.0"
                : "0.0.0";
}

/// <summary>Always reports no update. Used under the UI-test double, where hitting the real
/// GitHub API would make automated runs flaky and could pop an unexpected prompt mid-run.</summary>
public sealed class NullUpdateCheckService : IUpdateCheckService
{
    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new UpdateCheckResult(false, null, null, null));

    public Task DownloadInstallerAsync(
        string url, string destinationPath, IProgress<double> progress, CancellationToken cancellationToken) =>
        throw new NotSupportedException("No update is ever available from " + nameof(NullUpdateCheckService) + ".");

    public void LaunchInstallerAndExit(string installerPath) =>
        throw new NotSupportedException("No update is ever available from " + nameof(NullUpdateCheckService) + ".");
}
