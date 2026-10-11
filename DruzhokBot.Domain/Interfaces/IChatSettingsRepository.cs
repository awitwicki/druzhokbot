using DruzhokBot.Domain.Entities;

namespace DruzhokBot.Domain.Interfaces;

public interface IChatSettingsRepository
{
    Task<ChatSettings?> GetAsync(long chatId, CancellationToken cancellationToken = default);

    Task SetGreetingEnabledAsync(long chatId, bool enabled, CancellationToken cancellationToken = default);

    Task SetGreetingTextAsync(long chatId, string? text, CancellationToken cancellationToken = default);
}
