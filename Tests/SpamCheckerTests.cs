using DruzhokBot.Common.Helpers;
using Xunit;

namespace Tests;

public class SpamCheckerTests
{
    [Theory] 
    [InlineData("Привіт, я Дружок!\n\nДодай мене в свій чат, дай права opensea.io адміна, і я перевірятиму щоб група була завжди захищена від спам-ботів.\\n\\nВерсія: 1.1.0")]
    [InlineData("@Linaxyastrobot перевірятиму щоб група була завжди захищена від спам-ботів.\\n\\nВерсія: 1.1.0")]
    [InlineData("дай права @Lunaxnightbot адміна, і я перев")]
    [InlineData("@Lunarixprobot")]
    [InlineData("@Lunarixprobot\"")]
    [InlineData("дай права @Lunarixprobot прапрапр")]
    [InlineData("@Lunarixprobot прапрапр")]
    [InlineData("дай права @Lunarixprobot")]
    [InlineData("Привіт, я Дружок!\n\nДодай мене в свій @Phottoneurobot чат, дай права адміна, і я перевірятиму щоб група бу")]
    [InlineData("Привіт, t.me/Phottoneurobot")]
    [InlineData("Привіт, t.me/Phottoneurobot/")]
    [InlineData("Привіт, t.me/Phottoneurobot?")]
    [InlineData("Привіт, t.me/Phottoneurobot і я перевірятиму щоб група бу")]
    [InlineData("t.me/gogobot і я перевірятиму щоб група бу")]
    public void IsSpam_ShouldReturnTrue(string text)
    {
        // Arrange
        
        // Act
        var result = SpamChecker.IsSpam(text);
        
        // Assert
        Assert.True(result);
    }
    
    [Theory]
    [InlineData("@Lunarixprobotch")]
    [InlineData("@Lunarixprobotch\"")]
    [InlineData("@Lunarixprobotch адміна, і я перев")]
    [InlineData("отакий @Lunarixprobotch")]
    [InlineData("отакий @Lunarixprobotch адміна, і я перев")]
    [InlineData("Привіт, я Дружок!\n\nДодай мене в свій чат, дай права google.io адміна, і я перевірятиму щоб група була завжди захищена від спам-ботів.\\n\\nВерсія: 1.1.0")]
    [InlineData("Привіт,  дай права адміна, і я supersea.io перевірятиму щоб група була завжди захищена від спам-ботів.\\n\\nВерсія: 1.1.0")]
    [InlineData("Привіт, я Дружок!\n\nДодай мене в свій чат, дай права @bot адміна, і я перев")]
     [InlineData("Привіт, я Дружок!\n\nДодай мене в свій чат, дай права @Lunaxnight")]
    [InlineData("Привіт, я Дружок!\n\nДодай мене в свій @Phottoneurobotferma чат, дай права адміна, і я перевірятиму щоб група бу")]
    [InlineData("Привіт, t.me/Phottoneurobotoferma і я перевірятиму щоб група бу")]
    [InlineData("Привіт, t.me/gogobotio і я перевірятиму щоб група бу")]
    [InlineData("Привіт, t.me/Phottoneurobotoferma")]
    [InlineData("Привіт, t.me/Phottoneurobotoferma/")]
    public void IsSpam_ShouldReturnFalse(string text)
    {
        // Arrange
        
        // Act
        var result = SpamChecker.IsSpam(text);
        
        // Assert
        Assert.False(result);
    }
}
