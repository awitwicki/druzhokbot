using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DruzhokBot.Common.Data;

// Used by `dotnet ef` at design time to generate migrations.
public class BotDbContextFactory : IDesignTimeDbContextFactory<BotDbContext>
{
    public BotDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite("Data Source=druzhokbot.db")
            .Options;

        return new BotDbContext(options);
    }
}
