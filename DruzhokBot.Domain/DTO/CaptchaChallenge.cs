namespace DruzhokBot.Domain.DTO;

public record CaptchaChallenge(
    long UserId,
    long ChatId,
    string TargetEmoji,
    IReadOnlyList<CaptchaOption> Options,
    DateTime ExpiresAt);
