using DruzhokBot.Domain.Interfaces;
using NLog;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace DruzhokBot.Common.Services;

public class TelegramBotClientWrapper : ITelegramBotClientWrapper
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    
    private readonly TelegramBotClient _botClient;
    private Func<ITelegramBotClientWrapper, Update, CancellationToken, Task> UpdateHandlerDelegate;
    private Func<ITelegramBotClientWrapper, Exception, CancellationToken, Task> ErrorHandlerDelegate;
    private CancellationToken _cancellationToken;
    
    public TelegramBotClientWrapper(string token)
    {
        _botClient = new TelegramBotClient(token);
    }

    public Task<User> GetMeAsync(CancellationToken cancellationToken = default)
    {
        return _botClient.GetMe(cancellationToken: cancellationToken);
    }

    Task OnUpdate(Update update)
    {
        var task = new Task(() => { UpdateHandlerDelegate.Invoke(this, update, _cancellationToken); });
        task.Start();

        return task;
    }

    async Task OnError(Exception exception, HandleErrorSource source)
    {
        Console.WriteLine(exception);
        await Task.Delay(2000, _cancellationToken);
        
        Logger.Error(source.ToString());
        
        await ErrorHandlerDelegate.Invoke(this, exception, _cancellationToken);
    }

    public async Task DropPendingUpdates(CancellationToken cancellationToken = default)
    {
        await _botClient.DropPendingUpdates(cancellationToken);
    }
    
    public void SubscribeHandlers(
        Func<ITelegramBotClientWrapper, Update, CancellationToken, Task> updateHandler,
        Func<ITelegramBotClientWrapper, Exception, CancellationToken, Task> errorHandler,
        CancellationToken cancellationToken = default)
    {
        UpdateHandlerDelegate = updateHandler;
        ErrorHandlerDelegate = errorHandler;
        
        _botClient.OnUpdate += OnUpdate;
        _botClient.OnError += OnError;
    }

    public Task<Message> SendTextMessageAsync(
        ChatId chatId,
        string text,
        ParseMode parseMode = default,
        int? replyToMessageId = null,
        ReplyMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default)
    {
        return _botClient.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: parseMode,
            replyParameters: replyToMessageId.HasValue ? new ReplyParameters {MessageId = replyToMessageId.Value} : null,
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);
    }
    
    public Task DeleteMessageAsync(
        ChatId chatId,
        int messageId,
        CancellationToken cancellationToken = default)
    {
        return _botClient.DeleteMessage(chatId, messageId, cancellationToken);
    }

    public Task BanChatMemberAsync(ChatId chatId,
        long userId,
        DateTime? untilDate = null,
        bool revokeMessages = false,
        CancellationToken cancellationToken = default)
    {
        return _botClient.BanChatMember(
            chatId: chatId,
            userId: userId,
            untilDate: untilDate,
            revokeMessages: revokeMessages,
            cancellationToken: cancellationToken);
    }

    public Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool showAlert = true,
        string? url = null, int? cacheTime = null, CancellationToken cancellationToken = default)
    {
        return _botClient.AnswerCallbackQuery(callbackQueryId, text, showAlert, url, cacheTime, cancellationToken);
    }
}
