using System.Collections.Generic;
using System.Linq;
using DruzhokBot.Common.Helpers;
using Xunit;

namespace DruzhokBot.Tests;

public class EmojiPoolTests
{
    [Fact]
    public void Pool_Contains_22_Emojis()
    {
        Assert.Equal(22, EmojiPool.All.Count);
    }

    [Fact]
    public void Pool_Has_No_Duplicates()
    {
        Assert.Equal(EmojiPool.All.Count, EmojiPool.All.Distinct().Count());
    }

    [Fact]
    public void Every_Emoji_Has_NonEmpty_UkrainianName()
    {
        foreach (var emoji in EmojiPool.All)
        {
            var name = EmojiPool.GetUkrainianName(emoji);
            Assert.False(string.IsNullOrWhiteSpace(name), $"Missing name for {emoji}");
        }
    }

    [Fact]
    public void UkrainianNames_Are_Unique()
    {
        var names = EmojiPool.All.Select(EmojiPool.GetUkrainianName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void GetUkrainianName_UnknownEmoji_Throws()
    {
        Assert.Throws<KeyNotFoundException>(() => EmojiPool.GetUkrainianName("🦄"));
    }
}
