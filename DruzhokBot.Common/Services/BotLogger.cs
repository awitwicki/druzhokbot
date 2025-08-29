using DruzhokBot.Common.Extensions;
using DruzhokBot.Common.Helpers;
using DruzhokBot.Domain;
using DruzhokBot.Domain.DTO;
using DruzhokBot.Domain.Interfaces;
using Telegram.Bot.Types;

namespace DruzhokBot.Common.Services;

public class BotLogger : IBotLogger
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private static Dictionary<string, object> _buildLogsTableBase(User user, Chat chat, string eventType)
        => new ()
            {
                { "chat_name", chat.Title! },
                { "chat_username", chat.Username ?? "-" },
                { "chat_id", chat.Id },
                { "user_id", user.Id },
                { "user_name", user.Username ?? "-" },
                { "user_fullname", user.GetUserFullName() },
                { Consts.AppEventType, eventType }
            };
    
    public Task LogUserJoined(User user, Chat chat)
    {
        Logger.Info($"User {user.GetUserMention()} joined chat {chat.Title} ({chat.Id})");
        
        InfluxDbLiteClient.Query(
            Consts.AppLogsTableName, 
            _buildLogsTableBase(user, chat, Consts.AppEventTypeNewUser),
            new Dictionary<string, object>()
            {
                { Consts.AppEventValue, 1 }
            });
        
        return Task.CompletedTask;
    }

    public Task LogUserVerified(User user, Chat chat)
    {
        Logger.Info($"User {user.GetUserMention()} have successfully verified in chat {chat.Title} ({chat.Id})");
        
        InfluxDbLiteClient.Query(
            Consts.AppLogsTableName,
            _buildLogsTableBase(user, chat, Consts.AppEventTypeNewUserVerified),
            new Dictionary<string, object>()
            {
                { Consts.AppEventValue, 1 }
            });
        
        return Task.CompletedTask;
    }

    public Task LogUserBanned(UserBanQueueDto userBanDto)
    {
        Logger.Info($"User {userBanDto.User.GetUserMention()} banned from chat {userBanDto.Chat.Title} ({userBanDto.Chat.Id})");
        
        InfluxDbLiteClient.Query(
            Consts.AppLogsTableName, 
            _buildLogsTableBase(userBanDto.User, userBanDto.Chat, Consts.AppEventTypeBanUser),
            new Dictionary<string, object>()
            {
                { Consts.AppEventValue, 1 }
            });

        return Task.CompletedTask;
    }
    
    public Task LogRemoveSpam(Message message)
    {
        var messageText = message.Text ?? message.Caption;
        
        Logger.Info($"Message text from {message.Chat.Title}, {message.Chat.Username ?? "null"} ({message.Chat.Id}) by {message.From!.GetUserMention()} ({message.From!.Id}) removed. Text: {messageText}");
        
        InfluxDbLiteClient.Query(
            Consts.AppLogsTableName,
            _buildLogsTableBase(message.From, message.Chat, Consts.AppEventTypeRemoveSpam),
            new Dictionary<string, object>()
            {
                { Consts.RemovedMessageText, messageText! },
                { Consts.AppEventValue, 1 },
            });

        return Task.CompletedTask;
    }
}
