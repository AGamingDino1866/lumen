using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Lumen.Core.Gemini;

/// <summary>
/// Talks to the Google Generative Language API. This is the only type in Lumen that makes a
/// network request, and the only endpoint it ever calls is
/// <c>generativelanguage.googleapis.com</c>.
/// </summary>
/// <remarks>
/// The <see cref="HttpClient"/> is injected and owned by the caller for the application's
/// lifetime. Creating one per request would exhaust sockets under a multi-page run.
/// <para>
/// Concurrency is bounded by a <see cref="SemaphoreSlim"/> so a 200-page selection issues a few
/// requests at a time rather than two hundred at once.
/// </para>
/// </remarks>
public sealed class GeminiClient : IDisposable
{
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models";
    private const string ApiKeyHeader = "x-goog-api-key";

    private static readonly JsonSerializerOptions ResponseOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly RetryPolicy _retry;
    private readonly SemaphoreSlim _concurrency;

    /// <param name="http">Shared for the application lifetime.</param>
    /// <param name="retry">Backoff policy for transient failures.</param>
    /// <param name="maxConcurrency">Requests permitted in flight at once.</param>
    public GeminiClient(HttpClient http, RetryPolicy retry, int maxConcurrency = 4)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(retry);

        if (maxConcurrency < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), maxConcurrency, "At least one request must be permitted.");
        }

        _http = http;
        _retry = retry;
        _concurrency = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    /// <summary>
    /// Transcribes one rendered page. Never throws for an API-level failure; those are reported
    /// through <see cref="PageExtractionResult.ErrorMessage"/> so one bad page cannot end a run.
    /// Cancellation does propagate, because an aborted run should stop promptly.
    /// </summary>
    public async Task<PageExtractionResult> ExtractAsync(
        byte[] pngBytes,
        string model,
        string apiKey,
        string prompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        cancellationToken.ThrowIfCancellationRequested();

        var body = ExtractionRequestBuilder.BuildJson(pngBytes, prompt);

        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                HttpStatusCode status;
                string payload;

                try
                {
                    using var response = await SendAsync(body, model, apiKey, cancellationToken).ConfigureAwait(false);
                    status = response.StatusCode;
                    payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        return ParseResponse(payload);
                    }

                    var retryAfter = ReadRetryAfter(response.Headers);
                    if (_retry.ShouldRetry(status, attempt, retryAfter, out var delay))
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    return PageExtractionResult.Failed(GeminiErrorMapper.ToUserMessage(status));
                }
                catch (HttpRequestException)
                {
                    // A transport failure is transient by nature, so it gets the same budget
                    // as a 5xx before being reported.
                    if (_retry.ShouldRetry(HttpStatusCode.ServiceUnavailable, attempt, null, out var delay))
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    return PageExtractionResult.Failed(GeminiErrorMapper.NetworkFailure);
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // A client-side timeout, not a user cancellation.
                    if (_retry.ShouldRetry(HttpStatusCode.RequestTimeout, attempt, null, out var delay))
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    return PageExtractionResult.Failed(
                        GeminiErrorMapper.ToUserMessage(HttpStatusCode.RequestTimeout));
                }
            }
        }
        finally
        {
            _concurrency.Release();
        }
    }

    /// <summary>
    /// Checks a key by making one cheap request. Used before persisting a key, so an invalid one
    /// is reported inline at entry rather than as a failure on every page of the first run.
    /// </summary>
    /// <remarks>
    /// Returns the full <see cref="PageExtractionResult"/>, not a bare bool, so the caller can
    /// show the real reason a key was rejected — invalid key, rate limited, no access to this
    /// model, or a network failure all read identically as "false" otherwise, which leaves the
    /// user unable to tell a bad key from a transient problem.
    /// </remarks>
    public async Task<PageExtractionResult> ValidateKeyAsync(string apiKey, string model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return PageExtractionResult.Failed("Enter a key first.");
        }

        // A 1x1 transparent PNG: the smallest payload that still exercises the real
        // multimodal code path, including whether the key is permitted to use this model.
        var probe = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

        return await ExtractAsync(probe, model, apiKey, "Reply with the single word OK.", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(
        string body, string model, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{Endpoint}/{model}:generateContent")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        // The key travels as a header. Never as a query parameter, where it would be
        // recorded by proxies and server access logs.
        request.Headers.Add(ApiKeyHeader, apiKey);

        return await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseHeaders headers)
    {
        var retryAfter = headers.RetryAfter;

        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : null;
        }

        return null;
    }

    private static PageExtractionResult ParseResponse(string payload)
    {
        try
        {
            var response = JsonSerializer.Deserialize<GeminiResponse>(payload, ResponseOptions);

            if (response?.PromptFeedback?.BlockReason is { Length: > 0 })
            {
                return PageExtractionResult.Failed(GeminiErrorMapper.Blocked);
            }

            var candidate = response?.Candidates?.FirstOrDefault();
            var parts = candidate?.Content?.Parts;

            if (parts is null || parts.Count == 0)
            {
                // A blank page legitimately produces nothing. The prompt instructs the model to
                // return empty rather than invent content, so this is a success, not a failure.
                return PageExtractionResult.Ok(string.Empty);
            }

            var text = string.Concat(parts.Select(p => p.Text ?? string.Empty));
            return PageExtractionResult.Ok(text);
        }
        catch (JsonException)
        {
            return PageExtractionResult.Failed(GeminiErrorMapper.MalformedResponse);
        }
    }

    public void Dispose() => _concurrency.Dispose();
}
