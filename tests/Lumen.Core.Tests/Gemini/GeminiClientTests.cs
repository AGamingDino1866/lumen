using System.Net;
using System.Text;
using FluentAssertions;
using Lumen.Core.Gemini;
using Xunit;

namespace Lumen.Core.Tests.Gemini;

/// <summary>
/// Records the outgoing request and returns a scripted response. No test in this file
/// touches the network.
/// </summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

    public StubHandler(HttpStatusCode status, string body) => _responses.Enqueue((status, body));

    public StubHandler(params (HttpStatusCode Status, string Body)[] responses)
    {
        foreach (var r in responses) _responses.Enqueue(r);
    }

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastBody { get; private set; }
    public int CallCount { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        LastBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var (status, body) = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}

public class GeminiErrorMapperTests
{
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void Every_mapped_status_yields_a_human_sentence(HttpStatusCode status)
    {
        var message = GeminiErrorMapper.ToUserMessage(status);

        message.Should().NotBeNullOrWhiteSpace();
        message.Should().EndWith(".");
        message.Should().NotContain("Exception");
        message.Should().NotContain("HttpStatusCode");
    }

    [Fact]
    public void Invalid_key_message_points_at_settings()
    {
        GeminiErrorMapper.ToUserMessage(HttpStatusCode.Forbidden)
            .Should().Contain("key");
    }

    [Fact]
    public void Rate_limit_message_says_so_plainly()
    {
        GeminiErrorMapper.ToUserMessage(HttpStatusCode.TooManyRequests)
            .ToLowerInvariant().Should().Contain("rate");
    }

    [Fact]
    public void Unmapped_status_still_yields_a_sentence()
    {
        GeminiErrorMapper.ToUserMessage(HttpStatusCode.Conflict)
            .Should().NotBeNullOrWhiteSpace();
    }
}

public class GeminiClientTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];
    private const string Key = "AIzaSyTESTKEY0123456789";

    private static string SuccessJson(string text) =>
        $$"""
        { "candidates": [ { "content": { "parts": [ { "text": {{System.Text.Json.JsonSerializer.Serialize(text)}} } ] },
          "finishReason": "STOP" } ] }
        """;

    private static GeminiClient Client(StubHandler stub) =>
        new(new HttpClient(stub), new RetryPolicy(maxAttempts: 1, new Random(1)));

    [Fact]
    public async Task Sends_the_key_in_the_header_and_never_in_the_url()
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessJson("hello"));

        await Client(stub).ExtractAsync(Png, "gemini-2.5-flash", Key, "P", CancellationToken.None);

        stub.LastRequest!.RequestUri!.ToString().Should().NotContain(Key);
        stub.LastRequest.RequestUri.Query.Should().NotContain("key");
        stub.LastRequest.Headers.GetValues("x-goog-api-key").Should().ContainSingle().Which.Should().Be(Key);
    }

    [Fact]
    public async Task Posts_to_the_generate_content_endpoint_for_the_requested_model()
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessJson("hi"));

        await Client(stub).ExtractAsync(Png, "gemini-2.5-pro", Key, "P", CancellationToken.None);

        stub.LastRequest!.Method.Should().Be(HttpMethod.Post);
        stub.LastRequest.RequestUri!.ToString()
            .Should().Be("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-pro:generateContent");
    }

    [Fact]
    public async Task Parses_markdown_from_a_successful_response()
    {
        var result = await Client(new StubHandler(HttpStatusCode.OK, SuccessJson("# Title\n\nBody")))
            .ExtractAsync(Png, "m", Key, "P", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Markdown.Should().Be("# Title\n\nBody");
    }

    [Fact]
    public async Task Concatenates_multiple_text_parts()
    {
        const string json = """
        { "candidates": [ { "content": { "parts": [ { "text": "one " }, { "text": "two" } ] } } ] }
        """;

        (await Client(new StubHandler(HttpStatusCode.OK, json))
            .ExtractAsync(Png, "m", Key, "P", CancellationToken.None))
            .Markdown.Should().Be("one two");
    }

    [Fact]
    public async Task Empty_candidates_is_a_blank_page_not_an_error()
    {
        var result = await Client(new StubHandler(HttpStatusCode.OK, """{ "candidates": [] }"""))
            .ExtractAsync(Png, "m", Key, "P", CancellationToken.None);

        result.Success.Should().BeTrue("a blank page legitimately produces no text");
        result.Markdown.Should().BeEmpty();
    }

    [Fact]
    public async Task Malformed_json_yields_a_human_error_rather_than_an_exception()
    {
        var act = async () => await Client(new StubHandler(HttpStatusCode.OK, "{ not json"))
            .ExtractAsync(Png, "m", Key, "P", CancellationToken.None);

        await act.Should().NotThrowAsync();
        var result = await act();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Error_responses_map_to_a_message_and_never_leak_the_key(HttpStatusCode status)
    {
        var result = await Client(new StubHandler(status, """{"error":{"message":"boom"}}"""))
            .ExtractAsync(Png, "m", Key, "P", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        result.ErrorMessage.Should().NotContain(Key);
        result.ErrorMessage.Should().NotContain("AIza");
    }

    [Fact]
    public async Task Honours_a_cancelled_token()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await Client(new StubHandler(HttpStatusCode.OK, SuccessJson("x")))
            .ExtractAsync(Png, "m", Key, "P", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Retries_a_transient_failure_then_succeeds()
    {
        var stub = new StubHandler(
            (HttpStatusCode.ServiceUnavailable, "{}"),
            (HttpStatusCode.OK, SuccessJson("recovered")));
        var client = new GeminiClient(new HttpClient(stub), new RetryPolicy(maxAttempts: 3, new Random(1)));

        var result = await client.ExtractAsync(Png, "m", Key, "P", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Markdown.Should().Be("recovered");
        stub.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Does_not_retry_an_invalid_key()
    {
        var stub = new StubHandler(HttpStatusCode.Forbidden, "{}");
        var client = new GeminiClient(new HttpClient(stub), new RetryPolicy(maxAttempts: 3, new Random(1)));

        await client.ExtractAsync(Png, "m", Key, "P", CancellationToken.None);

        stub.CallCount.Should().Be(1, "a rejected key will be rejected again");
    }

    [Fact]
    public async Task Validate_key_returns_true_on_success()
    {
        (await Client(new StubHandler(HttpStatusCode.OK, SuccessJson("ok")))
            .ValidateKeyAsync(Key, "gemini-2.5-flash", CancellationToken.None))
            .Success.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_key_returns_false_on_rejection()
    {
        (await Client(new StubHandler(HttpStatusCode.Forbidden, "{}"))
            .ValidateKeyAsync(Key, "gemini-2.5-flash", CancellationToken.None))
            .Success.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_key_surfaces_the_real_rejection_reason()
    {
        (await Client(new StubHandler(HttpStatusCode.Forbidden, "{}"))
            .ValidateKeyAsync(Key, "gemini-2.5-flash", CancellationToken.None))
            .ErrorMessage.Should().Be("That API key was rejected. Check or replace your key in Settings.");
    }

    [Fact]
    public async Task Validate_key_surfaces_rate_limiting_distinctly_from_a_bad_key()
    {
        (await Client(new StubHandler(HttpStatusCode.TooManyRequests, "{}"))
            .ValidateKeyAsync(Key, "gemini-2.5-flash", CancellationToken.None))
            .ErrorMessage.Should().Be("Gemini is rate limiting this key. Wait a moment and retry the remaining pages.");
    }

    [Fact]
    public async Task Validate_key_surfaces_a_retired_model_distinctly_from_a_bad_key()
    {
        // A 404 here means Google no longer serves this model version at all -- a distinct,
        // actionable case from a rejected key, which previously fell into the same generic
        // catch-all message as every other unmapped status.
        (await Client(new StubHandler(HttpStatusCode.NotFound, "{}"))
            .ValidateKeyAsync(Key, "gemini-2.5-flash", CancellationToken.None))
            .ErrorMessage.Should().Contain("model").And.Contain("Settings");
    }

    [Fact]
    public async Task Unmapped_status_codes_include_the_code_itself()
    {
        (await Client(new StubHandler(HttpStatusCode.Conflict, "{}"))
            .ValidateKeyAsync(Key, "gemini-2.5-flash", CancellationToken.None))
            .ErrorMessage.Should().Contain("409");
    }

    // ------------------------------------------------------------------ rejected key as a 400

    /// <summary>
    /// The shape Google actually returns for a bad key: HTTP 400, not 401. Reading the status
    /// alone reported this as "the page may be malformed", which sends the user to inspect their
    /// PDF over what is really a two-character typo in their key.
    /// </summary>
    private const string InvalidKeyBody = """
        {
          "error": {
            "code": 400,
            "message": "API key not valid. Please pass a valid API key.",
            "status": "INVALID_ARGUMENT",
            "details": [ { "reason": "API_KEY_INVALID" } ]
          }
        }
        """;

    [Fact]
    public async Task A_rejected_key_returned_as_400_is_reported_as_a_key_problem()
    {
        var result = await Client(new StubHandler(HttpStatusCode.BadRequest, InvalidKeyBody))
            .ExtractAsync(Png, "m", Key, "P", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(GeminiErrorMapper.KeyRejected);
        result.ErrorMessage.Should().NotContain("malformed");
    }

    [Fact]
    public async Task Validating_a_bad_key_says_the_key_is_bad_and_never_echoes_it()
    {
        var result = await Client(new StubHandler(HttpStatusCode.BadRequest, InvalidKeyBody))
            .ValidateKeyAsync(Key, "gemini-2.5-flash", CancellationToken.None);

        result.ErrorMessage.Should().Be(GeminiErrorMapper.KeyRejected);
        result.ErrorMessage.Should().NotContain(Key);
    }

    [Fact]
    public async Task A_project_without_the_api_enabled_is_named_as_such()
    {
        const string body = """
            { "error": { "code": 403, "status": "PERMISSION_DENIED",
              "details": [ { "reason": "SERVICE_DISABLED" } ] } }
            """;

        (await Client(new StubHandler(HttpStatusCode.Forbidden, body))
            .ExtractAsync(Png, "m", Key, "P", CancellationToken.None))
            .ErrorMessage.Should().Be(GeminiErrorMapper.ApiNotEnabled);
    }

    [Fact]
    public async Task A_400_with_no_recognisable_reason_still_blames_the_page()
    {
        (await Client(new StubHandler(HttpStatusCode.BadRequest, """{"error":{"message":"boom"}}"""))
            .ExtractAsync(Png, "m", Key, "P", CancellationToken.None))
            .ErrorMessage.Should().Contain("page");
    }

    // ------------------------------------------------------------------ finishReason

    [Fact]
    public async Task A_truncated_reply_is_a_failure_not_a_silent_half_page()
    {
        const string body = """
            { "candidates": [ { "content": { "parts": [ { "text": "First half of the pa" } ] },
              "finishReason": "MAX_TOKENS" } ] }
            """;

        var result = await Client(new StubHandler(HttpStatusCode.OK, body))
            .ExtractAsync(Png, "m", Key, "P", CancellationToken.None);

        result.Success.Should().BeFalse("a transcription that silently loses the end of the page " +
                                        "is worse than one that reports it could not finish");
        result.ErrorMessage.Should().Be(GeminiErrorMapper.Truncated);
    }

    [Theory]
    [InlineData("SAFETY")]
    [InlineData("RECITATION")]
    [InlineData("PROHIBITED_CONTENT")]
    [InlineData("BLOCKLIST")]
    public async Task A_declined_page_is_reported_rather_than_transcribed_as_blank(string reason)
    {
        var body = $$"""{ "candidates": [ { "finishReason": "{{reason}}" } ] }""";

        var result = await Client(new StubHandler(HttpStatusCode.OK, body))
            .ExtractAsync(Png, "m", Key, "P", CancellationToken.None);

        result.Success.Should().BeFalse("a refused page arrives with no parts, exactly like a " +
                                       "blank one, and must not be exported as empty");
        result.ErrorMessage.Should().Be(GeminiErrorMapper.Blocked);
    }

    [Fact]
    public async Task A_stop_finish_reason_with_no_parts_is_still_a_blank_page()
    {
        const string body = """{ "candidates": [ { "finishReason": "STOP" } ] }""";

        (await Client(new StubHandler(HttpStatusCode.OK, body))
            .ExtractAsync(Png, "m", Key, "P", CancellationToken.None))
            .Success.Should().BeTrue();
    }

    // ------------------------------------------------------------------ transport failures

    /// <summary>Throws instead of answering, to stand in for a dead connection.</summary>
    private sealed class ThrowingHandler(Func<Exception> factory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromException<HttpResponseMessage>(factory());
        }
    }

    [Fact]
    public async Task A_dropped_connection_is_a_page_failure_not_an_exception()
    {
        // IOException, not HttpRequestException, is what a connection dropped mid-response
        // surfaces as. It used to escape ExtractAsync and end the whole run.
        var handler = new ThrowingHandler(() => new IOException("connection reset"));
        var client = new GeminiClient(new HttpClient(handler), new RetryPolicy(maxAttempts: 1, new Random(1)));

        var act = async () => await client.ExtractAsync(Png, "m", Key, "P", CancellationToken.None);

        await act.Should().NotThrowAsync();
        (await act()).ErrorMessage.Should().Be(GeminiErrorMapper.NetworkFailure);
    }

    [Fact]
    public async Task A_client_timeout_is_retried_and_then_reported_as_a_timeout()
    {
        // HttpClient reports its own timeout by throwing a cancellation exception with no
        // cancellation having been requested. Two things used to go wrong: the bare
        // OperationCanceledException form escaped as "the user cancelled", and RetryPolicy
        // classified 408 as permanent so the retry never happened.
        var handler = new ThrowingHandler(() => new OperationCanceledException("timeout"));
        var client = new GeminiClient(new HttpClient(handler), new RetryPolicy(maxAttempts: 3, new Random(1)));

        var result = await client.ExtractAsync(Png, "m", Key, "P", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(GeminiErrorMapper.ToUserMessage(HttpStatusCode.RequestTimeout));
        handler.CallCount.Should().Be(3, "a timeout is transient and must spend its retry budget");
    }

    [Fact]
    public async Task A_user_cancellation_still_propagates_rather_than_becoming_a_page_failure()
    {
        using var cts = new CancellationTokenSource();
        var handler = new ThrowingHandler(() =>
        {
            cts.Cancel();
            return new OperationCanceledException(cts.Token);
        });
        var client = new GeminiClient(new HttpClient(handler), new RetryPolicy(maxAttempts: 3, new Random(1)));

        var act = async () => await client.ExtractAsync(Png, "m", Key, "P", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ------------------------------------------------------------------ key hygiene

    [Theory]
    [InlineData("AIza-key-with-a\nnewline")]
    [InlineData("AIza-key-with-a\u200bzero-width-space")]
    [InlineData("AIza-key-with-a-\u00e9-accent")]
    public async Task A_key_that_cannot_travel_in_a_header_is_reported_not_thrown(string badKey)
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessJson("x"));

        var act = async () => await Client(stub).ExtractAsync(Png, "m", badKey, "P", CancellationToken.None);

        await act.Should().NotThrowAsync("HttpClient throws on such a header, and a pasted key " +
                                         "picking up a stray character is ordinary user error");
        (await act()).Success.Should().BeFalse();
        stub.CallCount.Should().Be(0, "nothing should go over the wire with an unusable key");
    }
}
