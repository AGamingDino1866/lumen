using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Lumen.Core.Security;

/// <summary>
/// Encrypts and decrypts a single secret at rest.
/// </summary>
public interface ISecretStore
{
    /// <summary>Encrypts <paramref name="plaintext"/>, returning base64, or null if there is nothing to store.</summary>
    string? Protect(string? plaintext);

    /// <summary>Decrypts base64 ciphertext, returning null if it cannot be decrypted by this user on this machine.</summary>
    string? Unprotect(string? cipherBase64);
}

/// <summary>
/// DPAPI-backed secret storage scoped to the current Windows user.
/// </summary>
/// <remarks>
/// Uses <see cref="DataProtectionScope.CurrentUser"/>, so the ciphertext is decryptable only by
/// this Windows user on this machine. Copying settings.json to another machine yields an
/// undecryptable blob rather than a leaked key.
/// <para>
/// Decryption failures are reported as <c>null</c> rather than exceptions: a tampered or
/// foreign-machine settings file is an expected condition that should re-open the key gate,
/// not crash the application.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : ISecretStore
{
    /// <summary>
    /// Additional entropy mixed into the DPAPI operation. This scopes the ciphertext to Lumen, so
    /// another application running as the same user cannot decrypt the stored key by chance.
    /// Changing this value invalidates every previously stored key.
    /// </summary>
    private static readonly byte[] Entropy = "Lumen.ApiKey.v1"u8.ToArray();

    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return null;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            var cipher = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(cipher);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    public string? Unprotect(string? cipherBase64)
    {
        if (string.IsNullOrWhiteSpace(cipherBase64))
        {
            return null;
        }

        try
        {
            var cipher = Convert.FromBase64String(cipherBase64);
            var bytes = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            // Not base64 at all: a hand-edited or truncated settings file.
            return null;
        }
        catch (CryptographicException)
        {
            // Tampered, or encrypted by a different user or machine.
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }
}
