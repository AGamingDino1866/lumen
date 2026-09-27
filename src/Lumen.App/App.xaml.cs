using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Lumen.App.Services;
using Lumen.App.ViewModels;
using Lumen.App.Views;
using Lumen.Core.Gemini;
using Lumen.Core.Pdf;
using Lumen.Core.Security;
using Lumen.Core.Settings;
using Application = System.Windows.Application;

namespace Lumen.App;

/// <summary>
/// Application entry point, composition root, and last line of defence against an unhandled
/// exception taking the window away without explanation.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// One HttpClient for the lifetime of the process. Creating one per request exhausts
    /// sockets under a multi-page run, and disposing one per request is worse. Built in
    /// <see cref="OnStartup"/> rather than at field-initialization time because its handler
    /// depends on <c>LUMEN_UI_TEST</c>, which must be read after the process environment is
    /// fully set up.
    /// </summary>
    private static HttpClient Http = null!;

    private ILogService _log = null!;

    /// <summary>
    /// The handler behind the shared <see cref="HttpClient"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="SocketsHttpHandler.PooledConnectionLifetime"/> is the reason this exists. A
    /// default HttpClient held for the life of the process never re-resolves DNS, and Lumen is a
    /// desktop app that stays open for days across sleep, VPN toggles and network changes — so a
    /// pooled connection to an address Google has since moved keeps being reused and every page
    /// fails until the user restarts. Recycling connections every two minutes bounds that to one
    /// stale attempt, which the retry policy already absorbs.
    /// <para>
    /// Decompression is enabled because transcriptions are plain text and gzip roughly halves
    /// what has to come down a slow connection.
    /// </para>
    /// </remarks>
    private static SocketsHttpHandler CreateHandler() => new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (TryRunNativeSelfTest(e.Args, out var exitCode))
        {
            Shutdown(exitCode);
            return;
        }

        Http = Environment.GetEnvironmentVariable("LUMEN_UI_TEST") == "1"
            ? new HttpClient(new StubGeminiHandler()) { Timeout = TimeSpan.FromMinutes(2) }
            : new HttpClient(CreateHandler()) { Timeout = TimeSpan.FromMinutes(2) };

        AppPaths.EnsureCreated();

        _log = new LogService(AppPaths.AppData);

        // Registered before anything else can throw, so even a start-up failure is reported
        // rather than closing the process silently.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        var settingsStore = new SettingsStore(AppPaths.AppData);
        var settings = settingsStore.Load();

        var themeService = new ThemeService(Resources.MergedDictionaries);
        themeService.Apply(ThemeService.Parse(settings.ThemeMode));

        var motionDictionary = Resources.MergedDictionaries
            .First(d => d.Contains("Lumen.Duration.BadgeIn"));
        var motionService = new MotionService(motionDictionary);

        var renderer = new PdfRenderService();
        var geminiClient = new GeminiClient(Http, new RetryPolicy(maxAttempts: 3));

        var viewModel = new MainViewModel(
            settingsStore,
            settings,
            new DpapiSecretStore(),
            renderer,
            geminiClient,
            new ThumbnailQueue(renderer, _log),
            new ClipboardService(_log),
            new DialogService(),
            themeService,
            motionService,
            _log);

        // A bare, non-flag argument is a PDF path: the "Open with Lumen" file association
        // invokes the exe as `Lumen.exe "%1"`, and the FlaUI suite uses the same mechanism to
        // open its fixture without automating the native file-open dialog.
        var pendingFilePath = e.Args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

        var window = new MainWindow(viewModel, new BackdropService(_log), themeService, _log, pendingFilePath);

        MainWindow = window;
        window.Show();

        _log.Info("Lumen started.");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log.Error("Unhandled UI exception.", e.Exception);

        // Handled = true keeps the application alive. An error in one command must not take the
        // user's open document and extracted text with it, since neither is persisted.
        e.Handled = true;

        ShowRecoverableError(e.Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log.Error("Unobserved background task exception.", e.Exception);

        // Observing the exception prevents it escalating; the run continues with the failed
        // page marked rather than the process ending.
        e.SetObserved();
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Nothing can be recovered at this point, but the log entry is what makes the crash
        // diagnosable afterwards.
        _log.Error("Fatal unhandled exception.", e.ExceptionObject as Exception);
    }

    private void ShowRecoverableError(Exception exception)
    {
        try
        {
            var dialog = new ErrorDialog(exception, _log.LogDirectory)
            {
                Owner = MainWindow is { IsLoaded: true } owner ? owner : null
            };

            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            // If even the error dialog fails, fall back to the system message box rather than
            // vanishing.
            _log.Error("The error dialog itself failed.", ex);

            MessageBox.Show(
                "Lumen hit an unexpected problem but is still running.",
                "Lumen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// A headless smoke test invoked only by build.ps1's native-DLL gate. Publishing as a
    /// self-contained single file changes how PDFium's and Skia's native libraries are located
    /// and loaded, and neither <c>dotnet run</c> nor a file-existence check exercises that path —
    /// the only reliable proof is launching the actual published exe and rendering a real page
    /// through it. The exit code is the pass/fail signal PowerShell reads; the result file exists
    /// only so a human can see why it failed without re-running under a debugger.
    /// </summary>
    private static bool TryRunNativeSelfTest(string[] args, out int exitCode)
    {
        exitCode = 0;

        var index = Array.IndexOf(args, "--self-test-render");
        if (index < 0 || index + 1 >= args.Length)
        {
            return false;
        }

        var resultPath = Path.Combine(AppContext.BaseDirectory, "self-test-result.txt");

        try
        {
            var renderer = new PdfRenderService();
            var info = renderer.Open(args[index + 1]);
            var png = renderer.RenderPagePng(args[index + 1], pageIndex: 0);

            var isPng = png is { Length: > 8 } &&
                        png[0] == 0x89 && png[1] == 0x50 && png[2] == 0x4E && png[3] == 0x47;

            if (isPng)
            {
                File.WriteAllText(resultPath, $"OK pages={info.PageCount} bytes={png.Length}");
                exitCode = 0;
            }
            else
            {
                File.WriteAllText(resultPath, "FAIL rendered output was not a valid PNG");
                exitCode = 1;
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(resultPath, $"FAIL {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            exitCode = 1;
        }

        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Info("Lumen exited.");

        // Null when exiting via the --self-test-render path, which returns before Http is built.
        Http?.Dispose();

        base.OnExit(e);
    }
}
