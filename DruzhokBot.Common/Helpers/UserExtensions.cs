using Telegram.Bot.Types;

namespace DruzhokBot.Common.Helpers;

public static class UserExtensions
{
    public static string GetUserFullName(this User user)
    {
        return user.FirstName + " " + user.LastName;
    }
}
