using System.Text.Json;
using FluentAssertions;
using Lumen.Core.Settings;
using Xunit;

namespace Lumen.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir;

    public SettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumen-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort cleanup */ }
    }

    [Fact]
    public void Returns_defaults_when_no_file_exists()
    {
        var settings = new SettingsStore(_dir).Load();

        settings.Model.Should().Be(LumenSettings.DefaultModel);
        settings.ThemeMode.Should().Be("System");
        settings.ExportPageBreaks.Should().BeTrue("the user chose page breaks as the default");
        settings.ExportPageHeadings.Should().BeTrue();
        settings.ProtectedApiKey.Should().BeNull();
        settings.RecentFiles.Should().BeEmpty();
    }

    [Fact]
    public void Round_trips_settings()
    {
        var store = new SettingsStore(_dir);
        var saved = store.Load();
        saved.Model = "gemini-3.1-pro-preview";
        saved.ThemeMode = "Dark";
        saved.SplitterPosition = 0.42;
        saved.RecentFiles.Add(@"C:\docs\report.pdf");
        store.Save(saved);

        var loaded = new SettingsStore(_dir).Load();

        loaded.Model.Should().Be("gemini-3.1-pro-preview");
        loaded.ThemeMode.Should().Be("Dark");
        loaded.SplitterPosition.Should().Be(0.42);
        loaded.RecentFiles.Should().ContainSingle().Which.Should().Be(@"C:\docs\report.pdf");
    }

    [Fact]
    public void Backs_up_and_recovers_from_corrupt_json()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ this is not valid json");

        var settings = new SettingsStore(_dir).Load();

        settings.Model.Should().Be(LumenSettings.DefaultModel, "a corrupt file must fall back to defaults");
        Directory.GetFiles(_dir, "*.corrupt").Should().NotBeEmpty("the bad file must be preserved for diagnosis");
    }

    [Theory]
    [InlineData("gemini-2.5-flash")] // retired by Google; guards against this exact regression
    [InlineData("gemini-1.0-pro")]
    [InlineData("")]
    [InlineData("not-a-model")]
    public void Unsupported_model_falls_back_to_the_default(string stored)
    {
        File.WriteAllText(
            Path.Combine(_dir, "settings.json"),
            $$"""{ "model": "{{stored}}" }""");

        // A model id that is not real is not a cosmetic problem: it goes into the request URL,
        // so every page of every run 404s and the app looks completely broken.
        new SettingsStore(_dir).Load().Model.Should().Be(LumenSettings.DefaultModel);
    }

    [Fact]
    public void Every_supported_model_survives_a_round_trip()
    {
        foreach (var model in LumenSettings.SupportedModels)
        {
            var store = new SettingsStore(_dir);
            var settings = store.Load();
            settings.Model = model;
            store.Save(settings);

            new SettingsStore(_dir).Load().Model.Should().Be(model);
        }
    }

    [Fact]
    public void Tolerates_unknown_fields()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            """{ "model": "gemini-3.1-pro-preview", "somethingFromAFutureVersion": 123 }""");

        var act = () => new SettingsStore(_dir).Load();

        act.Should().NotThrow();
        act().Model.Should().Be("gemini-3.1-pro-preview");
    }

    [Fact]
    public void Save_leaves_no_temporary_file_behind()
    {
        var store = new SettingsStore(_dir);
        store.Save(store.Load());

        Directory.GetFiles(_dir, "*.tmp").Should().BeEmpty("the atomic write must clean up after itself");
    }

    [Fact]
    public void Save_creates_the_directory_when_absent()
    {
        var nested = Path.Combine(_dir, "does", "not", "exist");
        var store = new SettingsStore(nested);

        var act = () => store.Save(new LumenSettings());

        act.Should().NotThrow();
        File.Exists(Path.Combine(nested, "settings.json")).Should().BeTrue();
    }

    [Fact]
    public void Persists_the_protected_key_but_never_a_plaintext_one()
    {
        var store = new SettingsStore(_dir);
        var settings = store.Load();
        settings.ProtectedApiKey = "Q2lwaGVyVGV4dEJhc2U2NA==";
        store.Save(settings);

        var json = File.ReadAllText(Path.Combine(_dir, "settings.json"));

        json.Should().Contain("Q2lwaGVyVGV4dEJhc2U2NA==");
        json.Should().NotContain("AIza", "a plaintext Google key must never reach disk");
    }

    [Fact]
    public void Written_json_is_valid_and_indented()
    {
        var store = new SettingsStore(_dir);
        store.Save(store.Load());

        var json = File.ReadAllText(Path.Combine(_dir, "settings.json"));

        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();
        json.Should().Contain("\n", "settings should stay human-readable");
    }
}

public class WindowPlacementTests
{
    private static readonly Rect2D Primary = new(0, 0, 1920, 1040);

    [Fact]
    public void Rect_fully_on_a_connected_monitor_is_visible()
    {
        WindowPlacement.IsRectVisible(new Rect2D(100, 100, 800, 600), new[] { Primary })
            .Should().BeTrue();
    }

    [Fact]
    public void Rect_on_an_unplugged_second_monitor_is_not_visible()
    {
        WindowPlacement.IsRectVisible(new Rect2D(3000, 100, 800, 600), new[] { Primary })
            .Should().BeFalse("the monitor it was saved on is gone");
    }

    [Fact]
    public void Rect_barely_clipping_a_corner_is_not_visible()
    {
        WindowPlacement.IsRectVisible(new Rect2D(1900, 1020, 800, 600), new[] { Primary })
            .Should().BeFalse("a few pixels of overlap is not a usable window position");
    }

    [Fact]
    public void Rect_mostly_on_screen_is_visible()
    {
        WindowPlacement.IsRectVisible(new Rect2D(-100, 0, 800, 600), new[] { Primary })
            .Should().BeTrue();
    }

    [Fact]
    public void Rect_on_a_still_connected_second_monitor_is_visible()
    {
        var secondary = new Rect2D(1920, 0, 1920, 1040);

        WindowPlacement.IsRectVisible(new Rect2D(2000, 100, 800, 600), new[] { Primary, secondary })
            .Should().BeTrue();
    }

    [Fact]
    public void Empty_monitor_list_is_never_visible()
    {
        WindowPlacement.IsRectVisible(new Rect2D(0, 0, 800, 600), Array.Empty<Rect2D>())
            .Should().BeFalse();
    }

    [Fact]
    public void Zero_sized_rect_is_not_visible()
    {
        WindowPlacement.IsRectVisible(new Rect2D(0, 0, 0, 0), new[] { Primary })
            .Should().BeFalse();
    }

    [Fact]
    public void Negative_sized_rect_is_not_visible()
    {
        WindowPlacement.IsRectVisible(new Rect2D(0, 0, -800, -600), new[] { Primary })
            .Should().BeFalse();
    }
}
