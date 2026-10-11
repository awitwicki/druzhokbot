using System;
using System.IO;
using System.Threading.Tasks;
using DruzhokBot.Common.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DruzhokBot.Tests;

public class ChatSettingsRepositoryTests : IDisposable
{
    private readonly string _directory;
    private readonly string _dbPath;
    private readonly DateTime _now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public ChatSettingsRepositoryTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "druzhokbot-tests-" + Guid.NewGuid().ToString("N"));
        _dbPath = Path.Combine(_directory, "nested", "druzhokbot.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private ChatSettingsRepository CreateRepository() => new(_dbPath, () => _now);

    [Fact]
    public void Constructor_CreatesDirectoryAndDatabaseFile()
    {
        CreateRepository();

        Assert.True(File.Exists(_dbPath));
    }

    [Fact]
    public async Task GetAsync_UnknownChat_ReturnsNull()
    {
        var repository = CreateRepository();

        Assert.Null(await repository.GetAsync(42));
    }

    [Fact]
    public async Task SetGreetingEnabledAsync_CreatesSettingsRow()
    {
        var repository = CreateRepository();

        await repository.SetGreetingEnabledAsync(42, true);

        var settings = await repository.GetAsync(42);
        Assert.NotNull(settings);
        Assert.True(settings!.GreetingEnabled);
        Assert.Null(settings.GreetingText);
        Assert.Equal(_now, settings.UpdatedAt);
    }

    [Fact]
    public async Task SetGreetingTextAsync_KeepsEnabledFlag()
    {
        var repository = CreateRepository();

        await repository.SetGreetingEnabledAsync(42, true);
        await repository.SetGreetingTextAsync(42, "Hello {user}");

        var settings = await repository.GetAsync(42);
        Assert.True(settings!.GreetingEnabled);
        Assert.Equal("Hello {user}", settings.GreetingText);
    }

    [Fact]
    public async Task SetGreetingTextAsync_Null_ClearsText()
    {
        var repository = CreateRepository();

        await repository.SetGreetingTextAsync(42, "Hello");
        await repository.SetGreetingTextAsync(42, null);

        Assert.Null((await repository.GetAsync(42))!.GreetingText);
    }

    [Fact]
    public async Task Settings_ArePerChat()
    {
        var repository = CreateRepository();

        await repository.SetGreetingEnabledAsync(1, true);
        await repository.SetGreetingTextAsync(2, "Other chat");

        Assert.True((await repository.GetAsync(1))!.GreetingEnabled);
        Assert.Null((await repository.GetAsync(1))!.GreetingText);
        Assert.False((await repository.GetAsync(2))!.GreetingEnabled);
        Assert.Equal("Other chat", (await repository.GetAsync(2))!.GreetingText);
    }

    [Fact]
    public async Task Settings_PersistAcrossRepositoryInstances()
    {
        await CreateRepository().SetGreetingTextAsync(42, "Persisted");

        var settings = await CreateRepository().GetAsync(42);

        Assert.Equal("Persisted", settings!.GreetingText);
    }
}
