using DruzhokBot.Domain.DTO;

namespace DruzhokBot.Domain.Interfaces;

public interface ICaptchaChallengeStore
{
    void Add(CaptchaChallenge challenge);
    CaptchaChallenge? TryGet(long userId, long chatId);
    void Remove(long userId, long chatId);
}
