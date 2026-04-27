using System;
using System.Threading.Tasks;
using DruzhokBot.Common.Services;
using Xunit;

namespace DruzhokBot.Tests;

public class AttackDetectorTests
{
    // Short poll interval keeps the lifetime task responsive to clock jumps during tests.
    private static readonly TimeSpan TestPollInterval = TimeSpan.FromMilliseconds(10);

    private sealed class FakeClock
    {
        public DateTime Now { get; set; } = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        public Func<DateTime> Func => () => Now;
    }

    [Fact]
    public void IsAngryModeActive_BeforeStart_ReturnsFalse()
    {
        var clock = new FakeClock();
        var detector = new AttackDetector(clock.Func, TestPollInterval);

        Assert.False(detector.IsAngryModeActive(chatId: 42));
    }

    [Fact]
    public async Task StartAngryMode_SetsIsAngryModeActiveTrueUntilExpiry()
    {
        var clock = new FakeClock();
        var detector = new AttackDetector(clock.Func, TestPollInterval);

        var lifetime = detector.StartAngryMode(chatId: 42);

        Assert.True(detector.IsAngryModeActive(42));

        // Advance past the initial duration; the lifetime task polls and completes.
        clock.Now += AttackDetector.InitialDuration + TimeSpan.FromSeconds(1);
        await lifetime.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(detector.IsAngryModeActive(42));
    }

    [Fact]
    public async Task StartAngryMode_WithNoBans_CompletesWithZeroCountAtInitialEndTime()
    {
        var clock = new FakeClock();
        var detector = new AttackDetector(clock.Func, TestPollInterval);
        var start = clock.Now;

        var lifetime = detector.StartAngryMode(chatId: 42);

        clock.Now = start + AttackDetector.InitialDuration + TimeSpan.FromMilliseconds(100);
        var finalState = await lifetime.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(42, finalState.ChatId);
        Assert.Equal(start, finalState.AttackStartTime);
        Assert.Equal(start + AttackDetector.InitialDuration, finalState.EndTime);
        Assert.Equal(0, finalState.BannedCount);
    }

    [Fact]
    public void RegisterJoin_FirstJoin_DoesNotTrigger()
    {
        var clock = new FakeClock();
        var detector = new AttackDetector(clock.Func, TestPollInterval);

        Assert.False(detector.RegisterJoin(chatId: 1));
    }

    [Fact]
    public void RegisterJoin_TwoJoinsWithin100s_DoesNotTrigger()
    {
        var clock = new FakeClock();
        var detector = new AttackDetector(clock.Func, TestPollInterval);

        Assert.False(detector.RegisterJoin(1));
        clock.Now += TimeSpan.FromSeconds(50);
        Assert.False(detector.RegisterJoin(1));
    }

    [Fact]
    public void RegisterJoin_ThreeJoinsWithin100s_Triggers()
    {
        var clock = new FakeClock();
        var detector = new AttackDetector(clock.Func, TestPollInterval);

        Assert.False(detector.RegisterJoin(1));
        clock.Now += TimeSpan.FromSeconds(30);
        Assert.False(detector.RegisterJoin(1));
        clock.Now += TimeSpan.FromSeconds(30);

        Assert.True(detector.RegisterJoin(1));
    }

    [Fact]
    public void RegisterJoin_ThreeJoinsSpanning101s_DoesNotTrigger()
    {
        var clock = new FakeClock();
        var detector = new AttackDetector(clock.Func, TestPollInterval);

        // First join falls outside the window by the time the third arrives.
        detector.RegisterJoin(1);
        clock.Now += TimeSpan.FromSeconds(60);
        detector.RegisterJoin(1);
        clock.Now += TimeSpan.FromSeconds(50); // total span = 110s, first entry pruned

        Assert.False(detector.RegisterJoin(1));
    }

    [Fact]
    public void RegisterJoin_DoesNotTriggerAgain_WhileAngryModeActive()
    {
        var clock = new FakeClock();
        var detector = new AttackDetector(clock.Func, TestPollInterval);

        detector.RegisterJoin(1);
        detector.RegisterJoin(1);
        Assert.True(detector.RegisterJoin(1));

        _ = detector.StartAngryMode(1);

        // Additional joins during active angry mode must not re-trigger.
        clock.Now += TimeSpan.FromSeconds(1);
        Assert.False(detector.RegisterJoin(1));
        clock.Now += TimeSpan.FromSeconds(1);
        Assert.False(detector.RegisterJoin(1));
    }

    [Fact]
    public void RegisterBanInAngryMode_FirstBan_IncrementsCountToOne()
    {
        var clock = new FakeClock();
        var detector = new AttackDetector(clock.Func, TestPollInterval);

        _ = detector.StartAngryMode(1);
        var updated = detector.RegisterBanInAngryMode(1);

        Assert.NotNull(updated);
        Assert.Equal(1, updated!.BannedCount);
    }

    [Fact]
    public void RegisterBanInAngryMode_WhenRemainingGte60s_DoesNotExtend()
    {
        var clock = new FakeClock();
        var detector = new AttackDetector(clock.Func, TestPollInterval);
        var start = clock.Now;

        _ = detector.StartAngryMode(1);
        // 2m into the 3m window; 60s remain exactly (not less than 60s).
        clock.Now = start + TimeSpan.FromMinutes(2);

        var updated = detector.RegisterBanInAngryMode(1);

        Assert.NotNull(updated);
        Assert.Equal(start + AttackDetector.InitialDuration, updated!.EndTime);
    }

    [Fact]
    public void RegisterBanInAngryMode_WhenRemainingLt60s_Extends45s()
    {
        var clock = new FakeClock();
        var detector = new AttackDetector(clock.Func, TestPollInterval);
        var start = clock.Now;

        _ = detector.StartAngryMode(1);
        // 2m01s into the 3m window; 59s remain.
        clock.Now = start + TimeSpan.FromSeconds(121);

        var updated = detector.RegisterBanInAngryMode(1);

        Assert.NotNull(updated);
        Assert.Equal(start + AttackDetector.InitialDuration + AttackDetector.ExtensionAmount,
            updated!.EndTime);
    }

    [Fact]
    public void RegisterBanInAngryMode_MultipleLateBans_AccumulateExtensions()
    {
        var clock = new FakeClock();
        var detector = new AttackDetector(clock.Func, TestPollInterval);
        var start = clock.Now;

        _ = detector.StartAngryMode(1);
        clock.Now = start + TimeSpan.FromSeconds(121); // 59s remain

        var after1 = detector.RegisterBanInAngryMode(1);     // +45s
        clock.Now += TimeSpan.FromSeconds(60);               // 44s remain
        var after2 = detector.RegisterBanInAngryMode(1);     // +45s

        Assert.NotNull(after2);
        Assert.Equal(start + AttackDetector.InitialDuration
            + AttackDetector.ExtensionAmount
            + AttackDetector.ExtensionAmount,
            after2!.EndTime);
        Assert.Equal(2, after2.BannedCount);
    }

    [Fact]
    public async Task RegisterBanInAngryMode_AfterAngryModeEnded_ReturnsNull()
    {
        var clock = new FakeClock();
        var detector = new AttackDetector(clock.Func, TestPollInterval);

        var lifetime = detector.StartAngryMode(1);
        clock.Now += AttackDetector.InitialDuration + TimeSpan.FromSeconds(1);
        await lifetime.WaitAsync(TimeSpan.FromSeconds(2));

        var result = detector.RegisterBanInAngryMode(1);

        Assert.Null(result);
    }

    [Fact]
    public async Task StartAngryMode_WithLateBan_CompletionDelayedByExtension()
    {
        var clock = new FakeClock();
        var detector = new AttackDetector(clock.Func, TestPollInterval);
        var start = clock.Now;

        var lifetime = detector.StartAngryMode(1);

        // Extend once in the final minute: remaining = 59s, becomes 104s.
        clock.Now = start + TimeSpan.FromSeconds(121);
        var extended = detector.RegisterBanInAngryMode(1);
        Assert.NotNull(extended);
        var extendedEnd = extended!.EndTime;

        // Jump to just before the extended end — the task must still be running.
        clock.Now = extendedEnd - TimeSpan.FromMilliseconds(50);
        // Give the poll loop a couple of iterations to observe the new state.
        await Task.Delay(TestPollInterval + TestPollInterval);
        Assert.False(lifetime.IsCompleted);

        // Past the extended end — the task completes with the extended state.
        clock.Now = extendedEnd + TimeSpan.FromMilliseconds(50);
        var finalState = await lifetime.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, finalState.BannedCount);
        Assert.Equal(extendedEnd, finalState.EndTime);
    }

    [Fact]
    public async Task RegisterJoin_TriggersAgain_AfterAngryModeEnds()
    {
        var clock = new FakeClock();
        var detector = new AttackDetector(clock.Func, TestPollInterval);

        detector.RegisterJoin(1);
        detector.RegisterJoin(1);
        Assert.True(detector.RegisterJoin(1));

        var lifetime = detector.StartAngryMode(1);
        clock.Now += AttackDetector.InitialDuration + TimeSpan.FromSeconds(1);
        await lifetime.WaitAsync(TimeSpan.FromSeconds(2));

        // Window from before was pruned during the wait; re-populate to re-trigger.
        detector.RegisterJoin(1);
        detector.RegisterJoin(1);
        Assert.True(detector.RegisterJoin(1));
    }
}
