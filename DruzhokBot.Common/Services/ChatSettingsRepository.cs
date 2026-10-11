using DruzhokBot.Common.Data;
using DruzhokBot.Domain.Entities;
using DruzhokBot.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DruzhokBot.Common.Services;

public class ChatSettingsRepository : IChatSettingsRepository
{
    private readonly DbContextOptions<BotDbContext> _options;
    private readonly Func<DateTime> _clock;

    public ChatSettingsRepository(string dbPath, Func<DateTime>? clock = null)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        _clock = clock ?? (() => DateTime.UtcNow);

        using var db = CreateContext();
        db.Database.Migrate();
    }

    private BotDbContext CreateContext() => new(_options);

    public async Task<ChatSettings?> GetAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext();
        return await db.ChatSettings.AsNoTracking()
            .SingleOrDefaultAsync(s => s.ChatId == chatId, cancellationToken);
    }

    public Task SetGreetingEnabledAsync(long chatId, bool enabled, CancellationToken cancellationToken = default)
        => UpsertAsync(chatId, s => s.GreetingEnabled = enabled, cancellationToken);

    public Task SetGreetingTextAsync(long chatId, string? text, CancellationToken cancellationToken = default)
        => UpsertAsync(chatId, s => s.GreetingText = text, cancellationToken);

    private async Task UpsertAsync(long chatId, Action<ChatSettings> apply, CancellationToken cancellationToken)
    {
        await using var db = CreateContext();

        var settings = await db.ChatSettings.SingleOrDefaultAsync(s => s.ChatId == chatId, cancellationToken);
        if (settings == null)
        {
            settings = new ChatSettings { ChatId = chatId };
            db.ChatSettings.Add(settings);
        }

        apply(settings);
        settings.UpdatedAt = _clock();

        await db.SaveChangesAsync(cancellationToken);
    }
}
