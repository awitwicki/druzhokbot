using Telegram.Bot.Types;

namespace DruzhokBot.Common.Helpers;

public static class UserChatNameHelper
{
    public static (string, string) ConvertUserChatName(User user, Chat chat)
    {
        var userFullName = (user.FirstName + " " + user.LastName).Replace(" ", "\\ ").Replace("=", "\\=");
        var chatTitle = (chat.Title).Replace(" ", "\\ ").Replace("=", "\\=");

        return (userFullName, chatTitle);
    }
}