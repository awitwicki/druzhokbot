using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace DruzhokBot.Domain.Interfaces;

public interface ITelegramBotClientWrapper
{
    Task<User> GetMeAsync(
        CancellationToken cancellationToken = default
    );
    
    void StartReceiving(
        Func<ITelegramBotClientWrapper, Update, CancellationToken, Task> updateHandler,
        Func<ITelegramBotClientWrapper, Exception, CancellationToken, Task> errorHandler,
        ReceiverOptions? receiverOptions = default,
        CancellationToken cancellationToken = default
    );

    Task<bool> SendTextMessageAsync2(ChatId chatId, string text, ParseMode? parseMode = default,
        IEnumerable<MessageEntity>? entities = default, bool? disableWebPagePreview = default,
        bool? disableNotification = default,
        int? replyToMessageId = default, bool? allowSendingWithoutReply = default,
        IReplyMarkup? replyMarkup = default,
        CancellationToken cancellationToken = default);
    
    Task<Message> SendTextMessageAsync(
        ChatId chatId,
        string text,
        ParseMode? parseMode = default,
        IEnumerable<MessageEntity>? entities = default,
        bool? disableWebPagePreview = default,
        bool? disableNotification = default,
        int? replyToMessageId = default,
        bool? allowSendingWithoutReply = default,
        IReplyMarkup? replyMarkup = default,
        CancellationToken cancellationToken = default
    );

    public Task DeleteMessageAsync(
        ChatId chatId,
        int messageId,
        CancellationToken cancellationToken = default
    );

    public Task BanChatMemberAsync(
        ChatId chatId,
        long userId,
        DateTime? untilDate = default,
        bool? revokeMessages = default,
        CancellationToken cancellationToken = default
    );

    public Task AnswerCallbackQueryAsync(
        string callbackQueryId,
        string? text = default,
        bool? showAlert = default,
        string? url = default,
        int? cacheTime = default,
        CancellationToken cancellationToken = default
    );
}
