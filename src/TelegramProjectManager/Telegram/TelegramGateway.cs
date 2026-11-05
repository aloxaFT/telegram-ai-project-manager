using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramProjectManager.Configuration;
using TelegramProjectManager.Domain;
using TelegramProjectManager.Services;

namespace TelegramProjectManager.Telegram;

public sealed record TelegramEnvelope(int UpdateId, IncomingUpdate? Input, string? CallbackQueryId);
public sealed class TelegramDeliveryException(int? errorCode, int retryAfter = 0) : Exception("Telegram request failed.")
{
    public int? ErrorCode { get; } = errorCode;
    public int RetryAfter { get; } = retryAfter;
}
public interface ITelegramGateway
{
    Task<IReadOnlyList<TelegramEnvelope>> Poll(int offset, CancellationToken ct);
    Task<long> Send(long chatId, string text, WorkItem? task, CancellationToken ct);
    Task Acknowledge(string callbackQueryId, CancellationToken ct);
}

public sealed class TelegramGateway : ITelegramGateway, IDisposable
{
    private readonly TelegramBotClient? _bot;
    private readonly HttpClient _http;
    public TelegramGateway(TelegramOptions options)
    {
        // No automatic HTTP retries: ambiguous sends must not cause duplicate notifications.
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(50) };
        if (options.Enabled) _bot = new TelegramBotClient(new TelegramBotClientOptions(options.Token) { RetryCount = 0 }, _http);
    }

    public async Task<IReadOnlyList<TelegramEnvelope>> Poll(int offset, CancellationToken ct)
    {
        if (_bot == null) return [];
        try
        {
            var updates = await _bot.GetUpdates(offset: offset, limit: 25, timeout: 30,
                allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery], cancellationToken: ct);
            return updates.Select(u =>
            {
                var message = u.Message ?? u.CallbackQuery?.Message;
                var user = u.CallbackQuery?.From ?? message?.From;
                IncomingUpdate? input = null;
                if (message != null && user != null && message.ForwardOrigin == null && message.SenderChat == null)
                    input = new IncomingUpdate(u.Id, message.Chat.Id, message.Chat.Type is ChatType.Group or ChatType.Supergroup,
                        user.Id, user.FirstName + (user.LastName == null ? "" : " " + user.LastName), user.Username,
                        message.Id, u.Message?.Text, u.CallbackQuery?.Data, user.IsBot, new DateTimeOffset(DateTime.SpecifyKind(message.Date, DateTimeKind.Utc)));
                return new TelegramEnvelope(u.Id, input, u.CallbackQuery?.Id);
            }).ToArray();
        }
        catch (ApiRequestException ex) { throw new TelegramDeliveryException(ex.ErrorCode, ex.Parameters?.RetryAfter ?? 0); }
        catch (HttpRequestException) { throw new TelegramDeliveryException(null); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TelegramDeliveryException(null); }
    }

    public async Task<long> Send(long chatId, string text, WorkItem? task, CancellationToken ct)
    {
        if (_bot == null) throw new InvalidOperationException("Telegram is disabled.");
        try
        {
            var message = await _bot.SendMessage(chatId, text, replyMarkup: task == null ? null : Keyboard(task),
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true }, cancellationToken: ct);
            return message.Id;
        }
        catch (ApiRequestException ex) { throw new TelegramDeliveryException(ex.ErrorCode, ex.Parameters?.RetryAfter ?? 0); }
        catch (HttpRequestException) { throw new TelegramDeliveryException(null); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TelegramDeliveryException(null); }
    }
    public async Task Acknowledge(string callbackQueryId, CancellationToken ct)
    {
        if (_bot == null) return;
        try { await _bot.AnswerCallbackQuery(callbackQueryId, cancellationToken: ct); }
        catch (ApiRequestException) { /* An expired callback acknowledgement does not undo a committed update. */ }
        catch (HttpRequestException) { }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }

    public static InlineKeyboardMarkup? Keyboard(WorkItem task)
    {
        InlineKeyboardButton Button(string text, WorkStatus status) => InlineKeyboardButton.WithCallbackData(text, $"t:{task.Number}:{status}");
        return task.Status switch
        {
            WorkStatus.Proposed => new InlineKeyboardMarkup(new InlineKeyboardButton[] {Button("Accept / claim", WorkStatus.Confirmed), Button("Reject", WorkStatus.Cancelled)}),
            WorkStatus.Confirmed => new InlineKeyboardMarkup(new InlineKeyboardButton[] {Button("Start", WorkStatus.InProgress), Button("Done", WorkStatus.Completed)}),
            WorkStatus.InProgress => new InlineKeyboardMarkup(new InlineKeyboardButton[] {Button("Blocked", WorkStatus.Blocked), Button("Done", WorkStatus.Completed)}),
            WorkStatus.Blocked => new InlineKeyboardMarkup(new InlineKeyboardButton[] {Button("Resume", WorkStatus.InProgress), Button("Done", WorkStatus.Completed)}),
            _ => null
        };
    }
    public void Dispose() => _http.Dispose();
}
