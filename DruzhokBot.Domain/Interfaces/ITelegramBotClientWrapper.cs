using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace DruzhokBot.Domain.Interfaces;

public interface ITelegramBotClientWrapper
{
    Task<User> GetMeAsync(
        CancellationToken cancellationToken = default
    );

    Task DropPendingUpdates(CancellationToken cancellationToken = default);
    
    void SubscribeHandlers(
        Func<ITelegramBotClientWrapper, Update, CancellationToken, Task> updateHandler,
        Func<ITelegramBotClientWrapper, Exception, CancellationToken, Task> errorHandler,
        CancellationToken cancellationToken = default
    );
    
    Task<Message> SendTextMessageAsync(
        ChatId chatId,
        string text,
        ParseMode parseMode = default,
        int? replyToMessageId = null,
        ReplyMarkup? replyMarkup = null,
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
        DateTime? untilDate = null,
        bool revokeMessages = false,
        CancellationToken cancellationToken = default
    );

    public Task AnswerCallbackQueryAsync(
        string callbackQueryId,
        string? text = null,
        bool showAlert = false,
        string? url = null,
        int? cacheTime = null,
        CancellationToken cancellationToken = default
    );
}
