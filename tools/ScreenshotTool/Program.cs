using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace Lumen.ScreenshotTool;

/// <summary>
/// Drives the real, built Lumen.exe through UI Automation and saves a PNG at each state required
/// by the definition of done. Not part of the shipped product or the automated test suite — a
/// re-runnable utility, invoked with <c>dotnet run --project tools/ScreenshotTool</c>.
/// </summary>
internal static class Program
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string OutDir = Path.Combine(RepoRoot, "docs", "screenshots");
    private static readonly string ExePath = ResolveExePath();
    private static readonly string FixturePdf = Path.Combine(RepoRoot, "tests", "Lumen.Core.Tests", "Fixtures", "sample.pdf");

    private static int Main()
    {
        Directory.CreateDirectory(OutDir);
        Console.WriteLine($"Exe:     {ExePath}");
        Console.WriteLine($"Fixture: {FixturePdf}");
        Console.WriteLine($"Out:     {OutDir}");

        RunAppFlow(themeMode: "Light", extractionDelayMs: 4000, captureSettings: true);
        RunAppFlow(themeMode: "Dark", extractionDelayMs: 0, captureSettings: false);
        CaptureInstallerWizard();

        Console.WriteLine("Done.");
        return 0;
    }

    /// <summary>
    /// One full session: start screen, open the fixture, select pages 1 and 3, extract, and land
    /// on the results with the Copy All confirmation. In light mode this also grabs "extraction
    /// in progress" (needs the artificial stub delay to have anything to catch) and the settings
    /// window. In dark mode it runs the same flow again purely to produce a genuinely distinct
    /// dark-mode screenshot of the same content, not a palette-swapped duplicate.
    /// </summary>
    private static void RunAppFlow(string themeMode, int extractionDelayMs, bool captureSettings)
    {
        if (themeMode == "Light")
        {
            // A separate, fixture-less launch for the true "before any document" state: driving
            // the native Open dialog via UI Automation (class "#32770", automation ID "1148")
            // turned out to be the least reliable part of this tool, so opening a document is
            // done by passing it on argv on a fresh launch instead, exactly like a double-click
            // via the "Open with Lumen" file association.
            using var startRun = LaunchIsolated(extractionDelayMs: 0, themeMode, passFixture: false);
            DismissApiKeyGate(startRun);
            var startWindow = startRun.WaitForWindow("Lumen");
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));
            SaveScreenshot(startWindow, "01-start-screen");
        }

        using var run = LaunchIsolated(extractionDelayMs, themeMode, passFixture: true);
        // The gate dialog is what actually appears first; MainWindow stays hidden behind it
        // (confirmed via raw EnumWindows) until the gate closes, so it must be dismissed before
        // searching for a window named "Lumen" or that search just times out.
        DismissApiKeyGate(run);
        var window = run.WaitForWindow("Lumen");

        var pageRangeBox = Retry.WhileNull(
            () => window.FindFirstDescendant(cf => cf.ByName("Page range")),
            TimeSpan.FromSeconds(10)).Result?.AsTextBox()
            ?? throw new InvalidOperationException("Page range box never appeared; the fixture did not open.");
        pageRangeBox.Focus();
        pageRangeBox.Text = "1, 3";
        Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(400));

        if (themeMode == "Light")
        {
            SaveScreenshot(window, "02-page-grid-mid-selection");
        }

        var extractButton = window.FindFirstDescendant(cf => cf.ByName("Extract text from the selected pages"))!.AsButton();
        extractButton.Invoke();

        if (extractionDelayMs > 0)
        {
            // A genuine pause, not FlaUI's Wait.UntilInputIsProcessed (which returns once the
            // input queue drains, not after a fixed duration) — the whole point is to land
            // mid-flight, comfortably inside the stub's artificial response delay.
            Thread.Sleep(Math.Min(1500, extractionDelayMs / 2));
            SaveScreenshot(window, "03-extraction-in-progress");
        }

        // "Copy all pages" is findable and enabled the instant the FIRST result row is added --
        // HasResults just means Results.Count > 0, set synchronously the moment extraction
        // starts, not when it finishes. Waiting only for the button's presence clicks Copy All
        // (and captures "done") while pages are still mid-flight. The real completion signal is
        // the progress text flipping to "Extracted N of N pages", which only happens once every
        // page has actually resolved.
        Retry.WhileNull(
            () => window.FindFirstDescendant(cf => cf.ByName("Extracted 2 of 2 pages")),
            TimeSpan.FromSeconds(Math.Max(20, extractionDelayMs / 1000.0 + 10)));

        var copyAllButton = window.FindFirstDescendant(cf => cf.ByName("Copy all pages"))!.AsButton();
        copyAllButton.Invoke();
        Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByName("Copied")), TimeSpan.FromSeconds(3));
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(150));

        SaveScreenshot(window, themeMode == "Light" ? "04-results-copied-confirmation" : "07-dark-theme");
        if (themeMode == "Light")
        {
            SaveScreenshot(window, "06-light-theme");
        }

        if (captureSettings)
        {
            var settingsButton = window.FindFirstDescendant(cf => cf.ByName("Settings"))!.AsButton();
            settingsButton.Invoke();
            var settingsWindow = Retry.WhileNull(
                () => run.Automation.GetDesktop().FindFirstDescendant(
                    cf => cf.ByName("Lumen settings").And(cf.ByControlType(ControlType.Window))),
                TimeSpan.FromSeconds(10)).Result?.AsWindow();

            if (settingsWindow is not null)
            {
                Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
                SaveScreenshot(settingsWindow, "05-settings");
            }
        }
    }

    private static void CaptureInstallerWizard()
    {
        var setupExe = Path.Combine(RepoRoot, "dist", $"Lumen-Setup-{ReadVersion()}.exe");
        if (!File.Exists(setupExe))
        {
            Console.WriteLine($"Skipping installer screenshot: {setupExe} not found. Run build.ps1 first.");
            return;
        }

        var psi = new ProcessStartInfo(setupExe) { UseShellExecute = false };
        using var proc = Process.Start(psi)!;
        using var automation = new UIA3Automation();

        try
        {
            // The launched exe is only a bootstrapper: Inno Setup re-extracts itself into a
            // "Lumen-Setup-x.y.z.tmp" child process, and the actual wizard window belongs to
            // that child's PID, not the one Process.Start returned. A loose title match (e.g.
            // "contains Lumen") is not safe here — it can and did match an unrelated window
            // (a terminal whose title happened to include the repo path) — so this instead
            // requires the window's *owning process* to actually be named "Lumen-Setup*".
            var wizard = Retry.WhileNull(
                () => automation.GetDesktop()
                    .FindAllChildren(cf => cf.ByControlType(ControlType.Window))
                    .FirstOrDefault(IsInstallerWindow),
                TimeSpan.FromSeconds(20)).Result?.AsWindow();

            if (wizard is null)
            {
                Console.WriteLine("Installer wizard window did not appear; skipping.");
                return;
            }

            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
            SaveScreenshot(wizard, "08-installer-wizard");
        }
        finally
        {
            // Cancel rather than complete: this tool must never leave Lumen actually installed.
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    // ===================== Helpers =====================

    private static bool IsInstallerWindow(AutomationElement window)
    {
        try
        {
            var pid = window.Properties.ProcessId.ValueOrDefault;
            var name = Process.GetProcessById(pid).ProcessName;
            return name.StartsWith("Lumen-Setup", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // The process may have exited between enumeration and lookup, or the property may
            // be unsupported for this element; either way it just isn't a match.
            return false;
        }
    }

    private static void DismissApiKeyGate(IsolatedRun run)
    {
        var gate = Retry.WhileNull(
            () => run.Automation.GetDesktop().FindFirstDescendant(cf => cf.ByName("Connect Lumen to Gemini")),
            TimeSpan.FromSeconds(15)).Result?.AsWindow();

        if (gate is null)
        {
            return;
        }

        var keyBox = gate.FindFirstDescendant(cf => cf.ByName("Gemini API key"))!.AsTextBox();
        keyBox.Enter("STUB-KEY-FOR-SCREENSHOTS");
        gate.FindFirstDescendant(cf => cf.ByName("Save and continue"))!.AsButton().Invoke();
        Retry.WhileTrue(() => gate.IsAvailable, TimeSpan.FromSeconds(10));
    }

    private static void SaveScreenshot(AutomationElement element, string name)
    {
        using var image = Capture.Element(element);
        var path = Path.Combine(OutDir, name + ".png");
        image.ToFile(path);
        Console.WriteLine($"Captured {name} -> {path}");
    }

    private static string ReadVersion()
    {
        var props = Path.Combine(RepoRoot, "Directory.Build.props");
        var doc = System.Xml.Linq.XDocument.Load(props);
        return doc.Descendants("Version").First().Value;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lumen.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root.");
    }

    private static string ResolveExePath()
    {
        var candidate = Path.Combine(RepoRoot, "src", "Lumen.App", "bin", "Release", "net8.0-windows", "win-x64", "Lumen.exe");
        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException("Build Lumen.App (Release) first.", candidate);
        }

        return candidate;
    }

    /// <summary>Launches Lumen with isolated settings and cleans up the process and temp dir on Dispose.</summary>
    private sealed class IsolatedRun(Process process, string appData) : IDisposable
    {
        public UIA3Automation Automation { get; } = new();

        public Window WaitForWindow(string name)
        {
            var element = Retry.WhileNull(
                () => Automation.GetDesktop().FindFirstDescendant(
                    cf => cf.ByName(name).And(cf.ByProcessId(process.Id))),
                TimeSpan.FromSeconds(20)).Result;

            return element?.AsWindow()
                ?? throw new InvalidOperationException($"Window '{name}' did not appear.");
        }

        public void Dispose()
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort.
            }

            Automation.Dispose();

            try
            {
                Directory.Delete(appData, recursive: true);
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    private static IsolatedRun LaunchIsolated(int extractionDelayMs, string themeMode, bool passFixture)
    {
        var appData = Path.Combine(Path.GetTempPath(), "LumenScreenshots-" + Guid.NewGuid());
        Directory.CreateDirectory(appData);
        File.WriteAllText(
            Path.Combine(appData, "settings.json"),
            $$"""{ "SchemaVersion": 1, "ThemeMode": "{{themeMode}}" }""");

        var psi = new ProcessStartInfo(ExePath, passFixture ? $"\"{FixturePdf}\"" : string.Empty)
        {
            UseShellExecute = false
        };
        psi.Environment["LUMEN_UI_TEST"] = "1";
        psi.Environment["LUMEN_APPDATA_OVERRIDE"] = appData;
        if (extractionDelayMs > 0)
        {
            psi.Environment["LUMEN_UI_TEST_DELAY_MS"] = extractionDelayMs.ToString();
        }

        var process = Process.Start(psi)!;
        return new IsolatedRun(process, appData);
    }
}
