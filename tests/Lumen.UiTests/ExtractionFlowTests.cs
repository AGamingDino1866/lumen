using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using FluentAssertions;

namespace Lumen.UiTests;

/// <summary>
/// Drives the real, built Lumen.exe through UI Automation: the API key gate, opening a PDF
/// handed to it on the command line, a non-contiguous page selection, extraction against the
/// offline stub (<c>LUMEN_UI_TEST=1</c>, see <c>StubGeminiHandler</c> in Lumen.App), the copy
/// confirmation, and opening the export dialog. Each run gets its own throwaway %APPDATA% via
/// <c>LUMEN_APPDATA_OVERRIDE</c> so it never touches a real user's saved key or recent files, and
/// never makes a real network call.
/// </summary>
public sealed class ExtractionFlowTests : IDisposable
{
    private readonly string _appDataOverride;
    private readonly Application _app;
    private readonly UIA3Automation _automation = new();

    public ExtractionFlowTests()
    {
        var exePath = ResolveExePath();
        var fixturePdf = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.pdf");
        File.Exists(fixturePdf).Should().BeTrue($"the sample fixture should have been copied to {fixturePdf}");

        _appDataOverride = Path.Combine(Path.GetTempPath(), "LumenUiTest-" + Guid.NewGuid());
        Directory.CreateDirectory(_appDataOverride);

        var psi = new ProcessStartInfo(exePath, $"\"{fixturePdf}\"")
        {
            UseShellExecute = false
        };
        psi.Environment["LUMEN_UI_TEST"] = "1";
        psi.Environment["LUMEN_APPDATA_OVERRIDE"] = _appDataOverride;

        _app = Application.Launch(psi);

        try
        {
            // Application.GetMainWindow polls Process.MainWindowHandle under the hood, which
            // this WPF app's window does not reliably populate even once it is genuinely visible
            // (confirmed directly with EnumWindows against a running instance). Searching the
            // desktop by process ID through UI Automation itself sidesteps that unreliable
            // heuristic and is what every FindFirstDescendant call below already relies on.
            var anyWindow = Retry.WhileNull(
                () => _automation.GetDesktop().FindFirstDescendant(cf => cf.ByProcessId(_app.ProcessId)),
                TimeSpan.FromSeconds(30)).Result;

            if (anyWindow is null)
            {
                throw new InvalidOperationException("No window for the Lumen process appeared within 30 seconds.");
            }
        }
        catch
        {
            // xUnit only calls Dispose() when the constructor returns successfully, so a failed
            // wait must kill the process here or it leaks an orphaned Lumen.exe.
            KillQuietly();
            throw;
        }
    }

    [Fact]
    public void Extracts_a_non_contiguous_selection_and_confirms_copy()
    {
        // 1. API key gate: a fresh, isolated %APPDATA% has no saved key, so the gate blocks.
        var gate = Retry.WhileNull(
            () => _automation.GetDesktop().FindFirstDescendant(cf => cf.ByName("Connect Lumen to Gemini")),
            TimeSpan.FromSeconds(10)).Result?.AsWindow();
        gate.Should().NotBeNull("the API key gate should appear on first launch");

        var keyBox = gate!.FindFirstDescendant(cf => cf.ByName("Gemini API key"))!.AsTextBox();
        keyBox.Enter("STUB-KEY-FOR-UI-TESTS");

        var saveButton = gate.FindFirstDescendant(cf => cf.ByName("Save and continue"))!.AsButton();
        saveButton.Invoke();

        // Validation is a real (stubbed) HTTP round trip; give it a moment to close the gate.
        Retry.WhileTrue(() => gate.IsAvailable, TimeSpan.FromSeconds(10));

        // 2. Now that the gate is gone, the shell ("Lumen") is the visible top-level window.
        // Found by name + process ID rather than Application.GetMainWindow(), which polls
        // Process.MainWindowHandle — a property this app's window does not reliably populate.
        var window = Retry.WhileNull(
            () => _automation.GetDesktop().FindFirstDescendant(
                cf => cf.ByName("Lumen").And(cf.ByProcessId(_app.ProcessId))),
            TimeSpan.FromSeconds(15)).Result?.AsWindow();
        window.Should().NotBeNull("the main shell window should be visible once the key gate closes");

        // 3. The fixture PDF was passed on the command line and should already be open.
        var pageRangeBox = Retry.WhileNull(
            () => window!.FindFirstDescendant(cf => cf.ByName("Page range")),
            TimeSpan.FromSeconds(10)).Result?.AsTextBox();
        pageRangeBox.Should().NotBeNull("a document should be open, exposing the page-range box");

        // 4. A non-contiguous selection: pages 1 and 3 of the 3-page fixture, skipping 2.
        pageRangeBox!.Focus();
        pageRangeBox.Text = "1, 3";
        Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.RETURN);

        var extractButton = window!.FindFirstDescendant(
            cf => cf.ByName("Extract text from the selected pages"))!.AsButton();
        extractButton.IsEnabled.Should().BeTrue("two pages are selected, so extraction should be available");
        extractButton.Invoke();

        // 5. Extraction runs against the offline stub, not the real Gemini API.
        var copyAllButton = Retry.WhileNull(
            () => window.FindFirstDescendant(cf => cf.ByName("Copy all pages")),
            TimeSpan.FromSeconds(20)).Result?.AsButton();
        copyAllButton.Should().NotBeNull("results should appear once the stubbed extraction completes");
        copyAllButton!.IsEnabled.Should().BeTrue();

        // 6. Copy confirmation: the button's label morphs to "Copied" for ~1.5s.
        copyAllButton.Invoke();
        var confirmed = Retry.WhileFalse(
            () => window.FindFirstDescendant(cf => cf.ByName("Copied")) is not null,
            TimeSpan.FromSeconds(3)).Result;
        confirmed.Should().BeTrue("the Copy All button should morph to a \"Copied\" confirmation");

        // 7. Export: the options dialog should open with both checkboxes ticked by default.
        var exportButton = window.FindFirstDescendant(cf => cf.ByName("Export"))!.AsButton();
        exportButton.Invoke();

        var exportDialog = Retry.WhileNull(
            () => _automation.GetDesktop().FindFirstDescendant(cf => cf.ByName("Export options")),
            TimeSpan.FromSeconds(10)).Result?.AsWindow();
        exportDialog.Should().NotBeNull("the export options dialog should open");

        var pageBreaks = exportDialog!.FindFirstDescendant(cf => cf.ByName("Page break between pages"))!.AsCheckBox();
        var pageHeadings = exportDialog.FindFirstDescendant(cf => cf.ByName("Heading per page"))!.AsCheckBox();
        pageBreaks.IsChecked.Should().BeTrue();
        pageHeadings.IsChecked.Should().BeTrue();

        // Cancel rather than driving the native Save dialog, which this suite does not automate.
        exportDialog.FindFirstDescendant(cf => cf.ByName("Cancel"))!.AsButton().Invoke();
    }

    /// <summary>
    /// Prefers a plain (non-single-file) build: it starts in well under a second, where the
    /// self-contained single-file publish self-extracts on every cold launch and can take long
    /// enough (antivirus scanning a fresh 200+MB payload, in particular) to blow past a
    /// reasonable UI-automation wait. That single-file path is exactly what build.ps1's native
    /// gate exists to verify separately; this suite's job is UI behaviour, not packaging, so a
    /// fast plain build is the right default. Set <c>LUMEN_EXE_PATH</c> to point at the
    /// published exe specifically if you need to test that build's UI too.
    /// </summary>
    private static string ResolveExePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("LUMEN_EXE_PATH");
        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lumen.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate the repo root from " + AppContext.BaseDirectory);
        }

        var candidates = new[]
        {
            Path.Combine(dir.FullName, "src", "Lumen.App", "bin", "Release", "net8.0-windows", "win-x64", "Lumen.exe"),
            Path.Combine(dir.FullName, "src", "Lumen.App", "bin", "Debug", "net8.0-windows", "win-x64", "Lumen.exe"),
            Path.Combine(dir.FullName, "src", "Lumen.App", "bin", "Release", "net8.0-windows", "win-x64", "publish", "Lumen.exe"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "Could not find Lumen.exe. Build or publish Lumen.App first, or set LUMEN_EXE_PATH.",
            candidates[0]);
    }

    private void KillQuietly()
    {
        try
        {
            _app.Close();
        }
        catch
        {
            // Best-effort: the process may already be gone.
        }

        try
        {
            _app.Dispose();
        }
        catch
        {
            // Best-effort.
        }
    }

    public void Dispose()
    {
        KillQuietly();
        _automation.Dispose();

        try
        {
            Directory.Delete(_appDataOverride, recursive: true);
        }
        catch
        {
            // A leftover temp directory is harmless.
        }
    }
}
