using System.Runtime.Versioning;
using FluentAssertions;
using Lumen.Core.Security;
using Xunit;

namespace Lumen.Core.Tests.Security;

// DPAPI is a Windows API. Lumen is a Windows application, so these tests are Windows-only
// by design; the annotation makes that explicit rather than suppressing the analyser.
[SupportedOSPlatform("windows")]
public class DpapiSecretStoreTests
{
    private const string SampleKey = "AIzaSyExample0123456789AbCdEfGhIjKlMnOp";

    [Fact]
    public void Round_trips_a_secret()
    {
        var store = new DpapiSecretStore();

        var cipher = store.Protect(SampleKey);

        cipher.Should().NotBeNullOrWhiteSpace();
        store.Unprotect(cipher!).Should().Be(SampleKey);
    }

    [Fact]
    public void Ciphertext_does_not_contain_the_plaintext()
    {
        var cipher = new DpapiSecretStore().Protect(SampleKey);

        cipher.Should().NotContain("AIza");
        cipher.Should().NotContain(SampleKey);
    }

    [Fact]
    public void Round_trips_unicode()
    {
        var store = new DpapiSecretStore();
        const string value = "kéy-wíth-ünicode-→";

        store.Unprotect(store.Protect(value)!).Should().Be(value);
    }

    [Fact]
    public void Returns_null_for_tampered_ciphertext()
    {
        new DpapiSecretStore().Unprotect("bm90LWEtcmVhbC1jaXBoZXJ0ZXh0").Should().BeNull();
    }

    [Fact]
    public void Returns_null_for_non_base64_input()
    {
        new DpapiSecretStore().Unprotect("!!! not base64 !!!").Should().BeNull();
    }

    [Fact]
    public void Returns_null_for_empty_input()
    {
        new DpapiSecretStore().Unprotect("").Should().BeNull();
    }

    [Fact]
    public void Protect_returns_null_for_empty_plaintext()
    {
        new DpapiSecretStore().Protect("").Should().BeNull();
    }

    [Fact]
    public void Two_protect_calls_produce_different_ciphertext()
    {
        // DPAPI salts each call, so identical plaintext must not yield identical bytes.
        var store = new DpapiSecretStore();

        store.Protect(SampleKey).Should().NotBe(store.Protect(SampleKey));
    }
}

public class SecretRedactorTests
{
    [Fact]
    public void Redacts_a_google_api_key()
    {
        var line = SecretRedactor.Redact("request failed with AIzaSyABCDEFGHIJKLMNOPQRST trailing");

        line.Should().NotContain("AIzaSy");
        line.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void Redacts_a_key_assignment()
    {
        SecretRedactor.Redact("api_key=super-secret-value-here")
            .Should().NotContain("super-secret-value-here");
    }

    [Fact]
    public void Redacts_the_x_goog_header_form()
    {
        SecretRedactor.Redact("x-goog-api-key: AIzaSyZZZZZZZZZZZZZZZZZZZZ")
            .Should().NotContain("AIzaSyZZZZ");
    }

    [Fact]
    public void Leaves_ordinary_lines_untouched()
    {
        const string line = "rendered page 4 of 7 in 182 ms";

        SecretRedactor.Redact(line).Should().Be(line);
    }

    [Fact]
    public void Handles_null_and_empty_without_throwing()
    {
        SecretRedactor.Redact("").Should().Be("");
        SecretRedactor.Redact(null!).Should().Be("");
    }

    [Fact]
    public void Redacts_every_occurrence_on_one_line()
    {
        var line = SecretRedactor.Redact("AIzaSyAAAAAAAAAAAAAAAAAAAA and AIzaSyBBBBBBBBBBBBBBBBBBBB");

        line.Should().NotContain("AIzaSyA");
        line.Should().NotContain("AIzaSyB");
    }
}
