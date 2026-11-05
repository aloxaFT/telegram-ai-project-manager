using Microsoft.EntityFrameworkCore;
using Npgsql;
using TelegramProjectManager.AI;
using TelegramProjectManager.Configuration;
using TelegramProjectManager.Persistence;
using TelegramProjectManager.Services;
using TelegramProjectManager.Telegram;

var builder = WebApplication.CreateBuilder(args);
var telegram = builder.Configuration.GetSection("Telegram").Get<TelegramOptions>() ?? new();
var ai = builder.Configuration.GetSection("Ai").Get<AiOptions>() ?? new();
var management = builder.Configuration.GetSection("Management").Get<ManagementOptions>() ?? new();
ConfigurationValidation.Validate(telegram, ai, management);
var connection = DatabaseConfiguration.GetConnectionString(builder.Configuration);
builder.Services.AddSingleton(telegram);
builder.Services.AddSingleton(ai);
builder.Services.AddSingleton(management);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<ProjectDbContext>(o => o.UseNpgsql(connection));
builder.Services.AddHttpClient<IAiAnalysisService, OpenAiCompatibleAnalysisService>(c => c.Timeout = TimeSpan.FromSeconds(45))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<ITelegramGateway, TelegramGateway>();
builder.Services.AddScoped<Outbox>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<AnalysisCoordinator>();
builder.Services.AddScoped<UpdateProcessor>();
builder.Services.AddScoped<ManagementScheduler>();
builder.Services.AddScoped<OutboxDispatcher>();
builder.Services.AddHostedService(sp => new PollingWorker(sp.GetRequiredService<IServiceScopeFactory>(), telegram,
    sp.GetRequiredService<ITelegramGateway>(), connection, sp.GetRequiredService<ILogger<PollingWorker>>()));
builder.Services.AddHostedService<ManagementWorker>();
builder.Services.AddHostedService<OutboxWorker>();
// HTTP request logs can contain provider-specific paths. Never log request bodies/options or Telegram token URLs.
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
var app = builder.Build();
var migrateOnly = args.Contains("--migrate-only", StringComparer.Ordinal);
if (migrateOnly || builder.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ProjectDbContext>().Database.MigrateAsync();
}
if (migrateOnly) return;
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (ProjectDbContext db, CancellationToken ct) =>
{
    try
    {
        var pending = await db.Database.GetPendingMigrationsAsync(ct);
        return !pending.Any() && await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "ready", telegramEnabled = telegram.Enabled, aiEnabled = ai.Enabled })
            : Results.StatusCode(503);
    }
    catch (Exception) { return Results.StatusCode(503); }
});
await app.RunAsync();
