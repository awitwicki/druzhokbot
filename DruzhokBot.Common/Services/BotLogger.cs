using DruzhokBot.Common.Extensions;
using DruzhokBot.Common.Helpers;
using DruzhokBot.Domain.DTO;
using DruzhokBot.Domain.Interfaces;
using Telegram.Bot.Types;

namespace DruzhokBot.Common.Services;

public class BotLogger : IBotLogger
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public Task LogUserJoined(User user, Chat chat)
    {
        (var userFullName, var chatTitle) = UserChatNameHelper.ConvertUserChatName(user, chat);
        var userName = user.Username ?? "none";
        var userId = user.Id;
        var chatId = chat.Id;

        InfluxDbLiteClient.Query(
            $"bots,botname=druzhokbot,chatname={chatTitle},chatusername={chat.Username ?? "null"},chat_id={chatId},user_id={userId},user_name={userName},user_fullname={userFullName} user_joined=1");

        return Task.CompletedTask;
    }

    public Task LogUserVerified(User user, Chat chat)
    {
        Logger.Info($"User {user.GetUserMention()} have successfully verified chat {chat.Title} ({chat.Id})");

        (var userFullName, var chatTitle) = UserChatNameHelper.ConvertUserChatName(user, chat);
        var userName = user.Username ?? "none";
        var userId = user.Id;
        var chatId = chat.Id;

        InfluxDbLiteClient.Query(
            $"bots,botname=druzhokbot,chatname={chatTitle},chatusername={chat.Username ?? "null"},chat_id={chatId},user_id={userId},user_name={userName},user_fullname={userFullName} user_verified=1");
        
        return Task.CompletedTask;
    }

    public Task LogUserBanned(UserBanQueueDto userBanDto)
    {
        (var userFullName, var chatTitle) = UserChatNameHelper.ConvertUserChatName(userBanDto.User, userBanDto.Chat);
        var userName = userBanDto.User.Username ?? "none";
        var userId = userBanDto.UserId;
        var chatId = userBanDto.ChatId;

        InfluxDbLiteClient.Query(
            $"bots,botname=druzhokbot,chatname={chatTitle},chat_id={chatId},user_id={userId},user_name={userName},user_fullname={userFullName} user_banned=1");
        
        return Task.CompletedTask;
    }
}
