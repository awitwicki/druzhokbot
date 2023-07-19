using DruzhokBot.Domain;

namespace DruzhokBot.Common.Helpers;

public static class CallbackDataStringBuilder
{
    public static string BuildBanUserCallbackData(long userId) => $"{Consts.BanUserString}|{userId}";
    public static string BuildNewUserCallbackData(long userId) => $"{Consts.NewUserString}|{userId}";
}
