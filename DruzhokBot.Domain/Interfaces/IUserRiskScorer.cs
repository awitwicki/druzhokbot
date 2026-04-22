using DruzhokBot.Domain.DTO;
using Telegram.Bot.Types;

namespace DruzhokBot.Domain.Interfaces;

public interface IUserRiskScorer
{
    Task<UserRiskAssessment> ScoreAsync(
        User user,
        ITelegramBotClientWrapper botClient,
        CancellationToken cancellationToken);
}
