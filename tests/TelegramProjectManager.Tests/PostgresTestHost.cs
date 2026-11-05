using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TelegramProjectManager.AI;
using TelegramProjectManager.Configuration;
using TelegramProjectManager.Domain;
using TelegramProjectManager.Persistence;
using TelegramProjectManager.Services;
using TelegramProjectManager.Telegram;

namespace TelegramProjectManager.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION")))
            Skip = "Set TEST_DATABASE_CONNECTION to a disposable PostgreSQL database to run integration tests.";
    }
}
public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-11-05T12:00:00Z");
    public override DateTimeOffset GetUtcNow() => Now;
}
public sealed class FakeAi : IAiAnalysisService
{
    public List<AnalysisContext> Calls { get; } = [];
    public Func<AnalysisContext, AnalysisResult>? Result { get; set; }
    public Task<AnalysisResult> Analyze(AnalysisContext context, CancellationToken ct)
    {
        Calls.Add(context);
        return Task.FromResult(Result?.Invoke(context) ?? new AnalysisResult
        {
            Tasks = [new TaskCandidate
            {
                Title = "Interview ACME", Description = "Customer discovery", OwnerTelegramUserId = 2,
                DueAt = null, Priority = "High", SourceMessageId = context.Messages.Last().TelegramMessageId,
                Confidence = 0.95, MissingInformation = []
            }], Decisions = [], Blockers = []
        });
    }
}
public sealed class FakeTelegram : ITelegramGateway
{
    public List<(long ChatId, string Text, WorkItem? Task)> Sent { get; } = [];
    public TelegramDeliveryException? Failure { get; set; }
    public Task<IReadOnlyList<TelegramEnvelope>> Poll(int offset, CancellationToken ct) => Task.FromResult<IReadOnlyList<TelegramEnvelope>>([]);
    public Task Acknowledge(string callbackQueryId, CancellationToken ct) => Task.CompletedTask;
    public Task<long> Send(long chatId, string text, WorkItem? task, CancellationToken ct)
    {
        if (Failure != null) throw Failure;
        Sent.Add((chatId, text, task));
        return Task.FromResult((long)(1000 + Sent.Count));
    }
}

public abstract class PostgresTestHost : IAsyncLifetime
{
    private readonly string _schema = "test_" + Guid.NewGuid().ToString("N");
    private string _baseConnection = "";
    private ServiceProvider? _services;
    private int _updateId;
    protected TestClock Clock { get; } = new();
    protected FakeAi Ai { get; } = new();
    protected FakeTelegram Telegram { get; } = new();
    protected ManagementOptions Management { get; } = new();
    protected AiOptions AiOptions { get; } = new() { Enabled = true, AutoAnalyzeMinutes = 15 };
    protected TelegramOptions TelegramOptions { get; } = new()
    {
        Enabled = true,
        Groups = [new() { ChatId = -100, ManagerUserIds = [1] }, new() { ChatId = -200, ManagerUserIds = [3], Name = "Other project" }]
    };

    public async Task InitializeAsync()
    {
        _baseConnection = Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION")!;
        await using var connection = new NpgsqlConnection(_baseConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{_schema}\"", connection);
        await command.ExecuteNonQueryAsync();
        var builder = new NpgsqlConnectionStringBuilder(_baseConnection) { SearchPath = _schema };
        var services = new ServiceCollection();
        services.AddDbContext<ProjectDbContext>(o => o.UseNpgsql(builder.ConnectionString));
        services.AddSingleton<TimeProvider>(Clock); services.AddSingleton(TelegramOptions);
        services.AddSingleton(Management); services.AddSingleton(AiOptions);
        services.AddSingleton<IAiAnalysisService>(Ai); services.AddSingleton<ITelegramGateway>(Telegram);
        services.AddScoped<Outbox>(); services.AddScoped<ReportService>(); services.AddScoped<AnalysisCoordinator>();
        services.AddScoped<UpdateProcessor>(); services.AddScoped<ManagementScheduler>(); services.AddScoped<OutboxDispatcher>();
        _services = services.BuildServiceProvider();
        await WithDb(async db => { await db.Database.MigrateAsync(); return true; });
    }
    public async Task DisposeAsync()
    {
        if (_services != null) await _services.DisposeAsync();
        if (string.IsNullOrWhiteSpace(_baseConnection)) return;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_baseConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }
    protected async Task<T> WithDb<T>(Func<ProjectDbContext, Task<T>> action)
    {
        await using var scope = _services!.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ProjectDbContext>());
    }
    protected async Task Process(IncomingUpdate input)
    {
        await using var scope = _services!.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<UpdateProcessor>().Process(input, default);
    }
    protected IncomingUpdate Input(long chat, long user, string? text = null, string? callback = null, int? messageId = null)
    {
        var id = Interlocked.Increment(ref _updateId);
        return new IncomingUpdate(id, chat, true, user, "Member " + user, "member" + user, messageId ?? id, text, callback);
    }
    protected async Task Send(long chat, long user, string text) => await Process(Input(chat, user, text));
    protected async Task SetupProject()
    {
        await Send(-100, 1, "/start"); await Send(-100, 1, "/join"); await Send(-100, 2, "/join");
    }
    protected async Task Tick()
    {
        await using var scope = _services!.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ManagementScheduler>().Tick(default);
    }
    protected async Task<bool> Dispatch()
    {
        await using var scope = _services!.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchOne(default);
    }
    protected async Task Drain()
    {
        for (var i = 0; i < 100 && await Dispatch(); i++) { }
    }
    protected async Task Propose()
    {
        await Send(-100, 2, "I will interview ACME tomorrow."); await Send(-100, 1, "/analyze");
    }
}
