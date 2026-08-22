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
    /// sockets under a multi-page run, and disposing one per request is worse.
    /// </summary>
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    private ILogService _log = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        var window = new MainWindow(viewModel, new BackdropService(_log), themeService, _log);

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

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Info("Lumen exited.");
        Http.Dispose();
        base.OnExit(e);
    }
}
