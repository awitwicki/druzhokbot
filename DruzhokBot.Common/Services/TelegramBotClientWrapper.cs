using DruzhokBot.Domain.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace DruzhokBot.Common.Services;

public class TelegramBotClientWrapper : ITelegramBotClientWrapper
{
    private readonly TelegramBotClient _botClient;
    private Func<ITelegramBotClientWrapper, Update, CancellationToken, Task> UpdateHandlerDelegate;
    private Func<ITelegramBotClientWrapper, Exception, CancellationToken, Task> ErrorHandlerDelegate;
    
    public TelegramBotClientWrapper(string token)
    {
        _botClient = new TelegramBotClient(token);
    }

    public Task<User> GetMeAsync(CancellationToken cancellationToken = default)
    {
        return _botClient.GetMeAsync(cancellationToken: cancellationToken);
    }

    public void StartReceiving(Func<ITelegramBotClientWrapper, Update, CancellationToken, Task> updateHandler,
        Func<ITelegramBotClientWrapper, Exception, CancellationToken, Task> errorHandler,
        ReceiverOptions? receiverOptions = default, CancellationToken cancellationToken = default)
    {
        UpdateHandlerDelegate = updateHandler;
        ErrorHandlerDelegate = errorHandler;
        
        _botClient.StartReceiving(updateHandler: HandleUpdateAsync, errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions, cancellationToken: cancellationToken);
    }

    public Task<Message> SendTextMessageAsync(ChatId chatId, string text, ParseMode? parseMode = default,
        IEnumerable<MessageEntity>? entities = default, bool? disableWebPagePreview = default, bool? disableNotification = default,
        int? replyToMessageId = default, bool? allowSendingWithoutReply = default, IReplyMarkup? replyMarkup = default,
        CancellationToken cancellationToken = default)
    {
        return _botClient.SendTextMessageAsync(chatId, text, parseMode, entities, disableWebPagePreview, disableNotification,
            replyToMessageId, allowSendingWithoutReply, replyMarkup, cancellationToken);
    }

    public Task<bool> SendTextMessageAsync2(ChatId chatId, string text, ParseMode? parseMode = default,
        IEnumerable<MessageEntity>? entities = default, bool? disableWebPagePreview = default, bool? disableNotification = default,
        int? replyToMessageId = default, bool? allowSendingWithoutReply = default, IReplyMarkup? replyMarkup = default,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    
    public Task DeleteMessageAsync(ChatId chatId, int messageId, CancellationToken cancellationToken = default)
    {
        return _botClient.DeleteMessageAsync(chatId, messageId, cancellationToken);
    }

    public Task BanChatMemberAsync(ChatId chatId, long userId, DateTime? untilDate = default, bool? revokeMessages = default,
        CancellationToken cancellationToken = default)
    {
        return _botClient.BanChatMemberAsync(chatId, userId, untilDate, revokeMessages, cancellationToken);
    }

    public Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = default, bool? showAlert = default,
        string? url = default, int? cacheTime = default, CancellationToken cancellationToken = default)
    {
        return _botClient.AnswerCallbackQueryAsync(callbackQueryId, text, showAlert, url, cacheTime, cancellationToken);
    }
    
    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            await UpdateHandlerDelegate.Invoke(this, update, cancellationToken);
        }
        catch (Exception exception)
        {
            await HandleErrorAsync(botClient, exception, cancellationToken);
        }
    }
    
    private async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        await ErrorHandlerDelegate.Invoke(this, exception, cancellationToken);
    }
}
