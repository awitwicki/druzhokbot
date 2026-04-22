using DruzhokBot.Domain;
using DruzhokBot.Domain.DTO;
using Telegram.Bot.Types.ReplyMarkups;

namespace DruzhokBot.Common.Helpers;

public static class CaptchaKeyboardBuilder
{
    public static InlineKeyboardMarkup BuildCaptchaKeyboard(CaptchaChallenge challenge)
    {
        var buttons = challenge.Options
            .Select(option => InlineKeyboardButton.WithCallbackData(
                option.Emoji,
                $"{Consts.CaptchaCallbackPrefix}|{option.Token}"))
            .ToList();

        return new InlineKeyboardMarkup(new[]
        {
            buttons.Take(3).ToArray(),
            buttons.Skip(3).Take(3).ToArray(),
        });
    }
}
