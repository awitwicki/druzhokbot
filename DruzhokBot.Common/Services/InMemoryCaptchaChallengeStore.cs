using System.Collections.Concurrent;
using DruzhokBot.Domain.DTO;
using DruzhokBot.Domain.Interfaces;

namespace DruzhokBot.Common.Services;

public class InMemoryCaptchaChallengeStore : ICaptchaChallengeStore
{
    private readonly ConcurrentDictionary<(long UserId, long ChatId), CaptchaChallenge> _challenges = new();

    public void Add(CaptchaChallenge challenge)
    {
        _challenges[(challenge.UserId, challenge.ChatId)] = challenge;
    }

    public CaptchaChallenge? TryGet(long userId, long chatId)
    {
        var key = (userId, chatId);
        if (!_challenges.TryGetValue(key, out var challenge))
            return null;

        if (challenge.ExpiresAt <= DateTime.UtcNow)
        {
            _challenges.TryRemove(key, out _);
            return null;
        }

        return challenge;
    }

    public void Remove(long userId, long chatId)
    {
        _challenges.TryRemove((userId, chatId), out _);
    }
}
