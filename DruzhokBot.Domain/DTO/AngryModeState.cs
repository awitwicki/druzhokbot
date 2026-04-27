namespace DruzhokBot.Domain.DTO;

public record AngryModeState(
    long ChatId,
    DateTime AttackStartTime,
    DateTime EndTime,
    int BannedCount);
