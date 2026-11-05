using Microsoft.EntityFrameworkCore;
using Npgsql;
using TelegramProjectManager.Configuration;
using TelegramProjectManager.Persistence;
using TelegramProjectManager.Services;

namespace TelegramProjectManager.Telegram;

public sealed class PollingWorker(IServiceScopeFactory scopes, TelegramOptions options,
    ITelegramGateway gateway, string connectionString, ILogger<PollingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var leader = new NpgsqlConnection(connectionString);
                await leader.OpenAsync(stoppingToken);
                await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(763819402)", leader);
                if (await command.ExecuteScalarAsync(stoppingToken) is not true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); continue;
                }
                logger.LogInformation("Telegram polling leader acquired.");
                while (!stoppingToken.IsCancellationRequested)
                {
                    // Keep the lock connection alive; stop polling immediately if leadership is lost.
                    command.CommandText = "SELECT 1";
                    await command.ExecuteScalarAsync(stoppingToken);
                    int offset;
                    await using (var scope = scopes.CreateAsyncScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<ProjectDbContext>();
                        offset = await db.PollingCursors.Where(c => c.Id == 1).Select(c => c.NextOffset).SingleOrDefaultAsync(stoppingToken);
                    }
                    var updates = await gateway.Poll(offset, stoppingToken);
                    foreach (var envelope in updates)
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<UpdateProcessor>().Process(envelope.Input ??
                            new IncomingUpdate(envelope.UpdateId, 0, false, 0, "", null, 0), stoppingToken);
                        if (envelope.CallbackQueryId != null) await gateway.Acknowledge(envelope.CallbackQueryId, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (TelegramDeliveryException ex)
            {
                logger.LogWarning("Telegram polling failed (code {Code}); retrying. Check bot credentials/webhook if persistent.", ex.ErrorCode);
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(ex.RetryAfter > 0 ? ex.RetryAfter : 10, 1, 300)), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError("Telegram update processing failed ({ErrorType}); offset retained for retry.", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
