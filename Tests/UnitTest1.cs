using System.Linq;
using DruzhokBot.Common.Helpers;
using Xunit;

namespace Tests
{
    public class UnitTest1
    {
        [Fact]
        public void AssertKeyboardBuilder()
        {
            var keyboardMarkup = CaptchaKeyboardBuilder.BuildCaptchaKeyboard(123);

            var keyboardButtons = keyboardMarkup
                .InlineKeyboard
                .SelectMany(x => x)
                .ToList();

            Assert.True(keyboardButtons.Count(x => x.CallbackData!.Contains("new_user")) == 1);
            Assert.True(keyboardButtons.Count(x => x.CallbackData!.Contains("ban_user")) == 5);
        }
    }
}
