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
}
