using System;
using System.Linq;
using DruzhokBot.Common.Helpers;
using Xunit;

namespace DruzhokBot.Tests;

public class CaptchaChallengeBuilderTests
{
    [Fact]
    public void Build_Returns_6_Distinct_Emoji_Options()
    {
        var challenge = CaptchaChallengeBuilder.Build(1, 1, TimeSpan.FromSeconds(90));

        Assert.Equal(6, challenge.Options.Count);
        Assert.Equal(6, challenge.Options.Select(o => o.Emoji).Distinct().Count());
    }

    [Fact]
    public void Build_Has_Exactly_One_Correct_Option()
    {
        var challenge = CaptchaChallengeBuilder.Build(1, 1, TimeSpan.FromSeconds(90));

        Assert.Single(challenge.Options.Where(o => o.IsCorrect));
    }

    [Fact]
    public void Build_TargetEmoji_Matches_Correct_Option_Emoji()
    {
        var challenge = CaptchaChallengeBuilder.Build(1, 1, TimeSpan.FromSeconds(90));

        var correct = challenge.Options.Single(o => o.IsCorrect);
        Assert.Equal(challenge.TargetEmoji, correct.Emoji);
    }

    [Fact]
    public void Build_Tokens_Are_Distinct_Within_Challenge()
    {
        var challenge = CaptchaChallengeBuilder.Build(1, 1, TimeSpan.FromSeconds(90));

        Assert.Equal(6, challenge.Options.Select(o => o.Token).Distinct().Count());
    }

    [Fact]
    public void Build_Tokens_Are_Sufficiently_Random_Across_Builds()
    {
        var tokens = Enumerable.Range(0, 100)
            .SelectMany(_ => CaptchaChallengeBuilder.Build(1, 1, TimeSpan.FromSeconds(90)).Options)
            .Select(o => o.Token)
            .ToList();

        // 100 challenges × 6 options = 600 tokens. Expect ≥ 599 distinct (allow 1 collision).
        Assert.True(tokens.Distinct().Count() >= 599,
            $"Token randomness too low: {tokens.Distinct().Count()}/600 distinct");
    }

    [Fact]
    public void Build_ExpiresAt_Is_Now_Plus_Ttl()
    {
        var before = DateTime.UtcNow;
        var challenge = CaptchaChallengeBuilder.Build(1, 1, TimeSpan.FromSeconds(90));
        var after = DateTime.UtcNow;

        Assert.InRange(challenge.ExpiresAt,
            before.AddSeconds(90).AddSeconds(-1),
            after.AddSeconds(90).AddSeconds(1));
    }

    [Fact]
    public void Build_Stores_UserId_And_ChatId()
    {
        var challenge = CaptchaChallengeBuilder.Build(42L, 99L, TimeSpan.FromSeconds(90));

        Assert.Equal(42L, challenge.UserId);
        Assert.Equal(99L, challenge.ChatId);
    }

    [Fact]
    public void Build_All_Option_Emojis_Come_From_EmojiPool()
    {
        var challenge = CaptchaChallengeBuilder.Build(1, 1, TimeSpan.FromSeconds(90));

        foreach (var option in challenge.Options)
            Assert.Contains(option.Emoji, EmojiPool.All);
    }
}
