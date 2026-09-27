using System.Net;

namespace Lumen.Core.Gemini;

/// <summary>
/// Decides whether a failed request should be retried and how long to wait first.
/// </summary>
/// <remarks>
/// This is a pure decision function: it computes a delay but never sleeps, which is what lets
/// the tests assert backoff behaviour instantly instead of waiting real seconds.
/// <para>
/// Backoff uses <b>full jitter</b> — a uniform draw between zero and the exponential ceiling —
/// rather than a fixed exponential delay. With several pages in flight against the same rate
/// limit, fixed delays would resynchronise every retry into a thundering herd and trip the
/// limit again on the same tick.
/// </para>
/// </remarks>
public sealed class RetryPolicy
{
    /// <summary>Ceiling for any single wait, including one requested by <c>Retry-After</c>.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    private readonly int _maxAttempts;
    private readonly Random _random;
    private readonly object _gate = new();

    /// <param name="maxAttempts">Total attempts including the first. 3 means one try plus two retries.</param>
    /// <param name="random">Injected so jitter is deterministic under test.</param>
    public RetryPolicy(int maxAttempts = 3, Random? random = null)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "At least one attempt is required.");
        }

        _maxAttempts = maxAttempts;
        _random = random ?? Random.Shared;
    }

    /// <summary>
    /// True when <paramref name="status"/> is transient and attempts remain.
    /// </summary>
    /// <param name="status">The status returned by the failed attempt.</param>
    /// <param name="attempt">1-based number of the attempt that just failed.</param>
    /// <param name="retryAfter">Value of the server's <c>Retry-After</c> header, if any.</param>
    /// <param name="delay">How long to wait before the next attempt.</param>
    public bool ShouldRetry(HttpStatusCode status, int attempt, TimeSpan? retryAfter, out TimeSpan delay)
    {
        delay = TimeSpan.Zero;

        if (attempt >= _maxAttempts || !IsTransient(status))
        {
            return false;
        }

        // An explicit Retry-After is the server telling us exactly when it will accept traffic
        // again. Honour it, but never let an absurd value hang the run.
        if (retryAfter is { } wait && wait > TimeSpan.Zero)
        {
            delay = wait > MaxDelay ? MaxDelay : wait;
            return true;
        }

        var ceilingSeconds = Math.Min(Math.Pow(2, attempt), MaxDelay.TotalSeconds);

        double factor;
        lock (_gate)
        {
            // Random is not thread-safe and several pages retry concurrently.
            factor = _random.NextDouble();
        }

        // Full jitter, with a small floor so a near-zero draw still yields a real pause.
        var seconds = Math.Max(0.05, ceilingSeconds * factor);
        delay = TimeSpan.FromSeconds(seconds);
        return true;
    }

    /// <summary>
    /// Only 408, 429 and 5xx are worth retrying. A 400 or 403 will fail identically on every
    /// attempt, so retrying wastes the user's time and quota.
    /// </summary>
    /// <remarks>
    /// 408 is here because <see cref="GeminiClient"/> reports a client-side timeout by asking
    /// about that status. Omitting it meant every timeout was classified as permanent and the
    /// retry budget the caller intended to spend on it was silently never used.
    /// </remarks>
    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)status >= 500;
}
