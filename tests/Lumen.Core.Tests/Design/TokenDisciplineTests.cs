using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Lumen.Core.Tests.Design;

/// <summary>
/// Enforces the three-layer token architecture at build time.
/// </summary>
/// <remarks>
/// "No hardcoded colours" is easy to state and easy to violate three weeks later. These tests
/// make it a property the build checks rather than a convention someone has to remember.
/// </remarks>
public class TokenDisciplineTests
{
    private static readonly Regex HexColour = new(
        @"#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})\b",
        RegexOptions.Compiled);

    private const string PrimitivesFile = "Primitives.xaml";

    /// <summary>Walks up from the test assembly until the directory containing Lumen.sln.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lumen.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the tests must be able to locate the repository root");
        return dir!.FullName;
    }

    private static List<string> XamlFiles()
    {
        var src = Path.Combine(RepoRoot(), "src");

        return Directory.Exists(src)
            ? Directory.EnumerateFiles(src, "*.xaml", SearchOption.AllDirectories).ToList()
            : [];
    }

    [Fact]
    public void There_is_xaml_to_check()
    {
        XamlFiles().Should().NotBeEmpty("otherwise this whole test class is silently vacuous");
    }

    [Fact]
    public void No_xaml_outside_Primitives_contains_a_hex_colour_literal()
    {
        var root = RepoRoot();

        var offenders = XamlFiles()
            .Where(f => !Path.GetFileName(f).Equals(PrimitivesFile, StringComparison.OrdinalIgnoreCase))
            .Select(f => new
            {
                File = Path.GetRelativePath(root, f),
                Matches = HexColour.Matches(File.ReadAllText(f)).Select(m => m.Value).Distinct().ToList()
            })
            .Where(x => x.Matches.Count > 0)
            .Select(x => $"{x.File}: {string.Join(", ", x.Matches)}")
            .ToList();

        offenders.Should().BeEmpty(
            "colours must come from semantic tokens so the light/dark swap is a single-layer change");
    }

    [Fact]
    public void Primitives_file_exists_and_defines_colours()
    {
        var primitives = XamlFiles()
            .FirstOrDefault(f => Path.GetFileName(f).Equals(PrimitivesFile, StringComparison.OrdinalIgnoreCase));

        primitives.Should().NotBeNull();
        HexColour.Matches(File.ReadAllText(primitives!)).Should().NotBeEmpty(
            "the primitives layer is where raw colour values belong");
    }

    /// <summary>
    /// Every declared &lt;Color&gt; value is a syntactically valid hex colour: exactly 3, 6, or 8
    /// hex digits, nothing more and nothing less.
    /// </summary>
    /// <remarks>
    /// <see cref="HexColour"/> only ever <em>finds</em> well-formed hex runs; it was never meant
    /// to prove a declared value is well-formed, and a malformed one (an extra stray byte, say)
    /// simply fails to match anywhere and is silently invisible to every other test in this
    /// class. This is what actually caught #FF33291180 -- ten hex digits, one too many -- which
    /// XamlParseException only surfaced at runtime, the first time dark mode tried to render a
    /// queued-page status chip. Lumen.Core cannot reference WPF, so this validates the hex
    /// grammar directly rather than asking System.Windows.Media.ColorConverter to parse it.
    /// </remarks>
    [Fact]
    public void Every_declared_colour_has_a_valid_hex_length()
    {
        var primitives = XamlFiles()
            .First(f => Path.GetFileName(f).Equals(PrimitivesFile, StringComparison.OrdinalIgnoreCase));

        var declared = Regex.Matches(
            File.ReadAllText(primitives),
            @"<Color\s+x:Key=""(?<key>[^""]+)"">(?<value>[^<]+)</Color>");

        declared.Should().NotBeEmpty("otherwise this test is silently vacuous");

        var malformed = declared
            .Cast<Match>()
            .Where(m => !Regex.IsMatch(
                m.Groups["value"].Value.Trim(),
                @"^#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})$"))
            .Select(m => $"{m.Groups["key"].Value} = {m.Groups["value"].Value.Trim()}")
            .ToList();

        malformed.Should().BeEmpty(
            "a hex colour must be exactly 3, 6, or 8 digits; anything else fails only at runtime, " +
            "inside whichever converter or binding first happens to touch it");
    }

    [Fact]
    public void Light_and_dark_semantic_layers_define_the_same_keys()
    {
        // A key present in one theme but not the other throws at theme-switch time rather than
        // at build time, which means it surfaces to the user rather than to the developer.
        var themes = Path.Combine(RepoRoot(), "src", "Lumen.App", "Themes");

        var light = ResourceKeys(Path.Combine(themes, "Semantic.Light.xaml"));
        var dark = ResourceKeys(Path.Combine(themes, "Semantic.Dark.xaml"));

        light.Should().NotBeEmpty();
        dark.Should().BeEquivalentTo(light,
            "every semantic token must resolve in both themes");
    }

    [Fact]
    public void Semantic_layers_do_not_reference_component_tokens()
    {
        var themes = Path.Combine(RepoRoot(), "src", "Lumen.App", "Themes");

        foreach (var file in new[] { "Semantic.Light.xaml", "Semantic.Dark.xaml" })
        {
            var text = File.ReadAllText(Path.Combine(themes, file));

            text.Should().NotContain("Lumen.PageTile.",
                "the semantic layer sits below components and must not depend upward");
            text.Should().NotContain("Lumen.Button.Radius");
        }
    }

    private static HashSet<string> ResourceKeys(string path)
    {
        File.Exists(path).Should().BeTrue($"{path} should exist");

        return Regex.Matches(File.ReadAllText(path), @"x:Key=""(?<key>[^""]+)""")
            .Select(m => m.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }
}
