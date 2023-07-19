using Telegram.Bot.Types.ReplyMarkups;

namespace DruzhokBot.Common.Helpers;

public static class CaptchaKeyboardBuilder
{
    private static InlineKeyboardButton GenerateBanButton(long userId)
    {
        return InlineKeyboardButton.WithCallbackData("🦾🤖", CallbackDataStringBuilder.BuildBanUserCallbackData(userId));
    }

    private static InlineKeyboardButton GenerateVerifyButton(long userId)
    {
        return InlineKeyboardButton.WithCallbackData("🚫🤖", CallbackDataStringBuilder.BuildNewUserCallbackData(userId));
    }

    public static InlineKeyboardMarkup BuildCaptchaKeyboard(long userId)
    {
        // Create ban buttons list
        var buttons = Enumerable.Range(1, 5)
            .Select(x => GenerateBanButton(userId))
            .ToList();

        // Add one right button
        buttons.Add(GenerateVerifyButton(userId));

        // Shuffle list
        buttons = buttons
            .OrderBy(a => Guid.NewGuid())
            .ToList();

        return new InlineKeyboardMarkup(new[]
        {               
            buttons.Take(3).ToArray(),
            buttons.Skip(3).Take(3).ToArray(),
        });
    }
}
