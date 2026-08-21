using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumen.Core.Settings;

/// <summary>
/// Loads and saves <see cref="LumenSettings"/> as JSON in a directory the caller chooses
/// (in the application, <c>%APPDATA%\Lumen</c>).
/// </summary>
/// <remarks>
/// Writes are atomic: the file is written to a temporary sibling and then moved into place, so a
/// crash or power loss mid-write cannot leave a truncated settings file. A file that fails to
/// parse is preserved with a <c>.corrupt</c> suffix and defaults are returned, because silently
/// deleting a user's settings, including their stored key, is worse than starting fresh with a
/// copy kept for diagnosis.
/// </remarks>
public sealed class SettingsStore
{
    private const string FileName = "settings.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // Unknown members are ignored by default, which is what lets an older build read a
        // settings file written by a newer one without throwing.
    };

    private readonly string _directory;

    public SettingsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <summary>Full path of the settings file, whether or not it currently exists.</summary>
    public string FilePath => Path.Combine(_directory, FileName);

    /// <summary>
    /// Returns the stored settings, or defaults when the file is absent, unreadable, or corrupt.
    /// Never throws.
    /// </summary>
    public LumenSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new LumenSettings();
            }

            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<LumenSettings>(json, Options);

            if (settings is null)
            {
                QuarantineCorruptFile();
                return new LumenSettings();
            }

            settings.RecentFiles ??= [];
            return settings;
        }
        catch (JsonException)
        {
            QuarantineCorruptFile();
            return new LumenSettings();
        }
        catch (IOException)
        {
            return new LumenSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new LumenSettings();
        }
    }

    /// <summary>
    /// Writes <paramref name="settings"/> atomically, creating the directory when necessary.
    /// </summary>
    public void Save(LumenSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(_directory);

        var json = JsonSerializer.Serialize(settings, Options);
        var temp = Path.Combine(_directory, $"{FileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temp, json);

            if (File.Exists(FilePath))
            {
                // File.Replace is atomic on NTFS and preserves the destination's identity.
                File.Replace(temp, FilePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temp, FilePath);
            }
        }
        finally
        {
            // If Replace or Move succeeded the temp file is already gone; this only cleans up
            // after a failure, so the directory never accumulates .tmp debris.
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch (IOException) { /* leave it rather than throw from Save */ }
            }
        }
    }

    private void QuarantineCorruptFile()
    {
        try
        {
            var target = Path.Combine(
                _directory,
                $"{FileName}.{DateTime.UtcNow:yyyyMMddHHmmss}.corrupt");

            File.Move(FilePath, target, overwrite: true);
        }
        catch (IOException)
        {
            // Preserving the bad file is best effort. Failing to do so must not stop start-up.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
