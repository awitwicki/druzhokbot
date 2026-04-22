using System;
using System.Collections.Generic;
using System.Linq;
using DruzhokBot.Common.Helpers;
using DruzhokBot.Domain;
using DruzhokBot.Domain.DTO;
using Xunit;

namespace Tests;

public class CaptchaKeyboardBuilderTests
{
    private static CaptchaChallenge MakeChallenge()
    {
        var options = new List<CaptchaOption>
        {
            new("tok-a", "🐶", false),
            new("tok-b", "🐈", true),
            new("tok-c", "🐸", false),
            new("tok-d", "🦊", false),
            new("tok-e", "🐼", false),
            new("tok-f", "🐷", false),
        };
        return new CaptchaChallenge(1, 2, "🐈", options, DateTime.UtcNow.AddSeconds(90));
    }

    [Fact]
    public void Keyboard_Has_6_Buttons_In_Two_Rows_Of_Three()
    {
        var keyboard = CaptchaKeyboardBuilder.BuildCaptchaKeyboard(MakeChallenge());

        var rows = keyboard.InlineKeyboard.ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(3, row.Count()));
    }

    [Fact]
    public void All_Buttons_Have_Captcha_Prefix()
    {
        var keyboard = CaptchaKeyboardBuilder.BuildCaptchaKeyboard(MakeChallenge());

        foreach (var button in keyboard.InlineKeyboard.SelectMany(r => r))
            Assert.StartsWith($"{Consts.CaptchaCallbackPrefix}|", button.CallbackData);
    }

    [Fact]
    public void No_Button_Payload_Reveals_Correctness()
    {
        var keyboard = CaptchaKeyboardBuilder.BuildCaptchaKeyboard(MakeChallenge());

        foreach (var button in keyboard.InlineKeyboard.SelectMany(r => r))
        {
            var data = button.CallbackData!;
            Assert.DoesNotContain("new_user", data);
            Assert.DoesNotContain("ban_user", data);
            Assert.DoesNotContain("correct", data);
            Assert.DoesNotContain("true", data);
        }
    }

    [Fact]
    public void Button_Tokens_Match_Challenge_Option_Tokens()
    {
        var challenge = MakeChallenge();
        var keyboard = CaptchaKeyboardBuilder.BuildCaptchaKeyboard(challenge);

        var buttonTokens = keyboard.InlineKeyboard
            .SelectMany(r => r)
            .Select(b => b.CallbackData!.Split('|')[1])
            .ToHashSet();
        var challengeTokens = challenge.Options.Select(o => o.Token).ToHashSet();

        Assert.Equal(challengeTokens, buttonTokens);
    }

    [Fact]
    public void All_Button_Payloads_Have_Identical_Shape()
    {
        var keyboard = CaptchaKeyboardBuilder.BuildCaptchaKeyboard(MakeChallenge());

        var segmentCounts = keyboard.InlineKeyboard
            .SelectMany(r => r)
            .Select(b => b.CallbackData!.Split('|').Length)
            .Distinct()
            .ToList();

        Assert.Single(segmentCounts);
        Assert.Equal(2, segmentCounts[0]);
    }

    [Fact]
    public void Button_Text_Is_Emoji_From_Option()
    {
        var challenge = MakeChallenge();
        var keyboard = CaptchaKeyboardBuilder.BuildCaptchaKeyboard(challenge);

        var buttonTexts = keyboard.InlineKeyboard.SelectMany(r => r).Select(b => b.Text).ToHashSet();
        var expectedEmojis = challenge.Options.Select(o => o.Emoji).ToHashSet();

        Assert.Equal(expectedEmojis, buttonTexts);
    }
}
