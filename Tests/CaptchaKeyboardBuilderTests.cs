using System.Linq;
using DruzhokBot.Common.Helpers;
using Xunit;

namespace Tests;

public class CaptchaKeyboardBuilderTests
{
    [Fact]
    public void AssertCaptchaKeyboardBuilder_ShouldContainExpectedValues()
    {
        // Arrange
        var keyboardMarkup = CaptchaKeyboardBuilder.BuildCaptchaKeyboard(123);

        // Act
        var keyboardButtons = keyboardMarkup
            .InlineKeyboard
            .SelectMany(x => x)
            .ToList();

        // Asserts
        Assert.True(keyboardButtons.Count(x => x.CallbackData!.Contains("new_user")) == 1);
        Assert.True(keyboardButtons.Count(x => x.CallbackData!.Contains("ban_user")) == 5);
    }
}
