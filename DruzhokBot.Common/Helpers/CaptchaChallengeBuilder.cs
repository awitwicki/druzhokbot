using System.Security.Cryptography;
using DruzhokBot.Domain.DTO;

namespace DruzhokBot.Common.Helpers;

public static class CaptchaChallengeBuilder
{
    private const int OptionCount = 6;
    private const int TokenByteLength = 9; // 9 bytes → 12 base64url chars

    public static CaptchaChallenge Build(long userId, long chatId, TimeSpan ttl)
    {
        var emojis = EmojiPool.All
            .OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue))
            .Take(OptionCount)
            .ToList();

        var correctIndex = RandomNumberGenerator.GetInt32(OptionCount);
        var targetEmoji = emojis[correctIndex];

        var options = emojis
            .Select((emoji, i) => new CaptchaOption(
                Token: GenerateToken(),
                Emoji: emoji,
                IsCorrect: i == correctIndex))
            .ToList();

        return new CaptchaChallenge(
            UserId: userId,
            ChatId: chatId,
            TargetEmoji: targetEmoji,
            Options: options,
            ExpiresAt: DateTime.UtcNow + ttl);
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
