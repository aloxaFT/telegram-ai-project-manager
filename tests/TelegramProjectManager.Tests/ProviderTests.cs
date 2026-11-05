using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TelegramProjectManager.AI;
using TelegramProjectManager.Configuration;
using TelegramProjectManager.Domain;

namespace TelegramProjectManager.Tests;

public sealed class ProviderTests
{
    private static AnalysisContext Context => new(DateTimeOffset.Parse("2026-11-05T12:00:00Z"), "Europe/Warsaw", "TradeFlow AI",
        [new ChatMessage { TelegramMessageId = 42, Text = "Let's consider interviewing ACME.", TelegramUserId = 2 }],
        [new ProjectMember { TelegramUserId = 2, DisplayName = "Team member", IsActive = true }]);
    private static AiOptions Options => new() { Enabled = true, Endpoint = "https://provider.invalid/v1/chat/completions", Model = "test-model", ApiKey = "unit-test-placeholder" };

    [Fact]
    public async Task Adapter_sends_bounded_context_with_provider_contract_and_parses_response()
    {
        var handler = new Handler(async request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("json_object", json.RootElement.GetProperty("response_format").GetProperty("type").GetString());
            var content = json.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            Assert.Contains("42", content); Assert.Contains("ACME", content);
            return JsonResponse("{\"tasks\":[],\"decisions\":[],\"blockers\":[]}");
        });
        using var http = new HttpClient(handler);
        var result = await new OpenAiCompatibleAnalysisService(http, Options).Analyze(Context, default);
        Assert.Empty(result.Tasks); Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Explicit_provider_rate_limit_is_retried_then_validated()
    {
        var handler = new Handler(_ => Task.FromResult(JsonResponse("{\"tasks\":[],\"decisions\":[],\"blockers\":[]}")));
        handler.FirstResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        handler.FirstResponse.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
        using var http = new HttpClient(handler);
        var result = await new OpenAiCompatibleAnalysisService(http, Options).Analyze(Context, default);
        Assert.Empty(result.Tasks); Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":{}}")]
    [InlineData("not json")]
    public async Task Malformed_provider_envelopes_are_rejected(string response)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) })));
        await Assert.ThrowsAsync<AnalysisValidationException>(() => new OpenAiCompatibleAnalysisService(http, Options).Analyze(Context, default));
    }

    [Fact]
    public async Task Disabled_provider_never_sends_a_request()
    {
        var handler = new Handler(_ => throw new InvalidOperationException());
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<AiUnavailableException>(() => new OpenAiCompatibleAnalysisService(http, new()).Analyze(Context, default));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Permanent_auth_failure_is_not_retried_or_echoed()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("sensitive diagnostic") }));
        using var http = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<AiUnavailableException>(() => new OpenAiCompatibleAnalysisService(http, Options).Analyze(Context, default));
        Assert.DoesNotContain("sensitive", exception.Message); Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void Render_database_URL_is_decoded_with_TLS_verification()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["DATABASE_URL"] = "postgres://db_user:unit%3Btest%3Apw@db.example.test:5433/project" }).Build();
        var builder = new NpgsqlConnectionStringBuilder(DatabaseConfiguration.GetConnectionString(config));
        Assert.Equal("db.example.test", builder.Host); Assert.Equal(5433, builder.Port);
        Assert.Equal("unit;test:pw", builder.Password); Assert.Equal(SslMode.VerifyFull, builder.SslMode);
    }

    [Fact]
    public void Database_URL_never_silently_disables_certificate_verification()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["DATABASE_URL"] = "postgres://user:placeholder@db.example.test/project?sslmode=disable" }).Build();
        Assert.Throws<InvalidOperationException>(() => DatabaseConfiguration.GetConnectionString(config));
    }

    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }), Encoding.UTF8, "application/json")
    };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpResponseMessage? FirstResponse { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Calls == 1 && FirstResponse != null ? Task.FromResult(FirstResponse) : respond(request);
        }
    }
}
