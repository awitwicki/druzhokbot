using DruzhokBot.Domain;
using DruzhokBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DruzhokBot.Common.Data;

public class BotDbContext : DbContext
{
    public BotDbContext(DbContextOptions<BotDbContext> options) : base(options)
    {
    }

    public DbSet<ChatSettings> ChatSettings => Set<ChatSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatSettings>(entity =>
        {
            entity.ToTable("chat_settings");
            entity.HasKey(e => e.ChatId);
            entity.Property(e => e.ChatId).ValueGeneratedNever();
            entity.Property(e => e.GreetingText).HasMaxLength(Consts.GreetingTextMaxLength);
        });
    }
}
