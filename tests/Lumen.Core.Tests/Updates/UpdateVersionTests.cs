using FluentAssertions;
using Lumen.Core.Updates;
using Xunit;

namespace Lumen.Core.Tests.Updates;

public class UpdateVersionTests
{
    [Fact]
    public void A_higher_patch_version_is_newer() =>
        UpdateVersion.IsNewer("1.0.1", "v1.0.2").Should().BeTrue();

    [Fact]
    public void The_same_version_is_not_newer() =>
        UpdateVersion.IsNewer("1.0.1", "v1.0.1").Should().BeFalse();

    [Fact]
    public void An_older_version_is_not_newer() =>
        UpdateVersion.IsNewer("1.0.2", "v1.0.1").Should().BeFalse();

    [Fact]
    public void A_four_part_file_version_matches_the_equivalent_three_part_tag() =>
        UpdateVersion.IsNewer("1.0.1.0", "v1.0.1").Should().BeFalse();

    [Fact]
    public void The_v_prefix_is_optional() =>
        UpdateVersion.IsNewer("1.0.1", "1.0.2").Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void Unparsable_candidate_is_never_newer(string? candidate) =>
        UpdateVersion.IsNewer("1.0.1", candidate).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void Unparsable_current_version_is_never_older(string? current) =>
        UpdateVersion.IsNewer(current, "v1.0.1").Should().BeFalse();
}
