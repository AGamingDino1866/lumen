namespace Lumen.Core.Updates;

/// <summary>
/// Compares a GitHub release tag against the running app's version.
/// </summary>
/// <remarks>
/// Normalizes both sides to Major.Minor.Build before comparing: the app's real FileVersion is
/// four-part ("1.0.1.0", the trailing zero required by Windows but meaningless to Lumen), while a
/// release tag is written by hand as "v1.0.1" or "1.0.1". <see cref="Version"/> treats a missing
/// part as -1, not 0, so comparing those two forms unnormalized would report "1.0.1" older than
/// "1.0.1.0" even though they name the same release.
/// </remarks>
public static class UpdateVersion
{
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length > 0 && (trimmed[0] == 'v' || trimmed[0] == 'V'))
        {
            trimmed = trimmed[1..];
        }

        if (!Version.TryParse(trimmed, out var parsed))
        {
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    /// <summary>True when <paramref name="candidateTag"/> names a version newer than <paramref name="currentVersionText"/>.</summary>
    /// <remarks>Unparsable input on either side is treated as "no update" rather than throwing.</remarks>
    public static bool IsNewer(string? currentVersionText, string? candidateTag)
    {
        if (!TryParse(currentVersionText, out var current) || !TryParse(candidateTag, out var candidate))
        {
            return false;
        }

        return candidate > current;
    }

    private static Version Normalize(Version v) =>
        new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}
