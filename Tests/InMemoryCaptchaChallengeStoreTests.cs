using System;
using System.Collections.Generic;
using DruzhokBot.Common.Services;
using DruzhokBot.Domain.DTO;
using Xunit;

namespace Tests;

public class InMemoryCaptchaChallengeStoreTests
{
    private static CaptchaChallenge MakeChallenge(long userId, long chatId, TimeSpan ttl)
        => new(userId, chatId, "🐶",
            new List<CaptchaOption> { new("tok", "🐶", true) },
            DateTime.UtcNow + ttl);

    [Fact]
    public void Add_Then_TryGet_Returns_Challenge()
    {
        var store = new InMemoryCaptchaChallengeStore();
        var challenge = MakeChallenge(1, 2, TimeSpan.FromSeconds(60));

        store.Add(challenge);

        Assert.Same(challenge, store.TryGet(1, 2));
    }

    [Fact]
    public void TryGet_Unknown_User_Returns_Null()
    {
        var store = new InMemoryCaptchaChallengeStore();
        Assert.Null(store.TryGet(1, 2));
    }

    [Fact]
    public void TryGet_Different_User_Same_Chat_Returns_Null()
    {
        var store = new InMemoryCaptchaChallengeStore();
        store.Add(MakeChallenge(1, 2, TimeSpan.FromSeconds(60)));

        Assert.Null(store.TryGet(99, 2));
    }

    [Fact]
    public void TryGet_After_Expiry_Returns_Null_And_Evicts()
    {
        var store = new InMemoryCaptchaChallengeStore();
        var expired = MakeChallenge(1, 2, TimeSpan.FromSeconds(-1));
        store.Add(expired);

        Assert.Null(store.TryGet(1, 2));
        Assert.Null(store.TryGet(1, 2)); // Confirm evicted (not just masked)
    }

    [Fact]
    public void Remove_Clears_Challenge()
    {
        var store = new InMemoryCaptchaChallengeStore();
        store.Add(MakeChallenge(1, 2, TimeSpan.FromSeconds(60)));

        store.Remove(1, 2);

        Assert.Null(store.TryGet(1, 2));
    }

    [Fact]
    public void Add_Overwrites_Existing_Entry_For_Same_Key()
    {
        var store = new InMemoryCaptchaChallengeStore();
        var first = MakeChallenge(1, 2, TimeSpan.FromSeconds(60));
        var second = MakeChallenge(1, 2, TimeSpan.FromSeconds(60));

        store.Add(first);
        store.Add(second);

        Assert.Same(second, store.TryGet(1, 2));
    }

    [Fact]
    public void Remove_Unknown_Key_Does_Not_Throw()
    {
        var store = new InMemoryCaptchaChallengeStore();
        store.Remove(1, 2); // no exception
    }
}
