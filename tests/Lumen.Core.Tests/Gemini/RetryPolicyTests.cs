using System.Net;
using FluentAssertions;
using Lumen.Core.Gemini;
using Xunit;

namespace Lumen.Core.Tests.Gemini;

/// <summary>
/// These tests assert backoff behaviour without ever sleeping: the policy is a pure decision
/// function returning a delay, and the caller is responsible for awaiting it.
/// </summary>
public class RetryPolicyTests
{
    private static RetryPolicy Policy(int maxAttempts = 3) =>
        new(maxAttempts, new Random(Seed: 1234));

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void Retries_on_429_and_5xx(HttpStatusCode status)
    {
        Policy().ShouldRetry(status, attempt: 1, retryAfter: null, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public void Does_not_retry_on_client_errors(HttpStatusCode status)
    {
        Policy().ShouldRetry(status, attempt: 1, retryAfter: null, out _).Should().BeFalse(
            "a bad key or malformed request will fail identically on every attempt");
    }

    [Fact]
    public void Does_not_retry_a_success()
    {
        Policy().ShouldRetry(HttpStatusCode.OK, 1, null, out _).Should().BeFalse();
    }

    [Fact]
    public void Gives_up_once_max_attempts_is_reached()
    {
        Policy(maxAttempts: 3).ShouldRetry(HttpStatusCode.TooManyRequests, attempt: 3, null, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Retries_up_to_but_not_beyond_max_attempts()
    {
        var policy = Policy(maxAttempts: 3);

        policy.ShouldRetry(HttpStatusCode.ServiceUnavailable, 1, null, out _).Should().BeTrue();
        policy.ShouldRetry(HttpStatusCode.ServiceUnavailable, 2, null, out _).Should().BeTrue();
        policy.ShouldRetry(HttpStatusCode.ServiceUnavailable, 3, null, out _).Should().BeFalse();
    }

    [Fact]
    public void Retry_after_header_wins_over_computed_backoff()
    {
        Policy().ShouldRetry(HttpStatusCode.TooManyRequests, 1, TimeSpan.FromSeconds(7), out var delay);

        delay.Should().Be(TimeSpan.FromSeconds(7), "the server told us exactly how long to wait");
    }

    [Fact]
    public void Retry_after_is_clamped_to_the_ceiling()
    {
        Policy().ShouldRetry(HttpStatusCode.TooManyRequests, 1, TimeSpan.FromHours(1), out var delay);

        delay.Should().BeLessThanOrEqualTo(RetryPolicy.MaxDelay,
            "an absurd Retry-After must not hang the run");
    }

    [Fact]
    public void Negative_retry_after_is_ignored()
    {
        Policy().ShouldRetry(HttpStatusCode.TooManyRequests, 1, TimeSpan.FromSeconds(-5), out var delay);

        delay.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void Backoff_is_positive_and_capped()
    {
        Policy().ShouldRetry(HttpStatusCode.ServiceUnavailable, attempt: 2, null, out var delay);

        delay.Should().BeGreaterThan(TimeSpan.Zero);
        delay.Should().BeLessThanOrEqualTo(RetryPolicy.MaxDelay);
    }

    [Fact]
    public void Backoff_grows_with_the_attempt_number()
    {
        // Full jitter makes any single pair unreliable, so compare averages across many draws.
        var policy = Policy(maxAttempts: 10);

        var first = Enumerable.Range(0, 500).Select(_ =>
        {
            policy.ShouldRetry(HttpStatusCode.ServiceUnavailable, 1, null, out var d);
            return d.TotalMilliseconds;
        }).Average();

        var third = Enumerable.Range(0, 500).Select(_ =>
        {
            policy.ShouldRetry(HttpStatusCode.ServiceUnavailable, 3, null, out var d);
            return d.TotalMilliseconds;
        }).Average();

        third.Should().BeGreaterThan(first);
    }

    [Fact]
    public void Backoff_is_jittered_rather_than_fixed()
    {
        var policy = Policy(maxAttempts: 10);

        var draws = Enumerable.Range(0, 50).Select(_ =>
        {
            policy.ShouldRetry(HttpStatusCode.ServiceUnavailable, 3, null, out var d);
            return d;
        }).Distinct().ToList();

        draws.Should().HaveCountGreaterThan(1,
            "identical delays would synchronise every client into a thundering herd");
    }

    [Fact]
    public void Retries_a_request_timeout()
    {
        // GeminiClient reports a client-side timeout by asking about this status. Classifying it
        // as permanent meant every timeout consumed none of the retry budget it was given.
        new RetryPolicy(maxAttempts: 3, new Random(1))
            .ShouldRetry(HttpStatusCode.RequestTimeout, attempt: 1, retryAfter: null, out var delay)
            .Should().BeTrue();

        delay.Should().BePositive();
    }
}
